#!/usr/bin/env python3
"""
Create GitHub issues from a folder of Markdown files using GitHub CLI (gh).

Requirements:
- GitHub CLI installed and already authenticated: `gh auth status` should be OK.
- Run this script from the ROOT of a local Git repository (or pass --repo-root).
- Files should be Markdown (.md). Optional YAML-like front matter supported:
  ---
  labels: [label1, label2, fase-3]
  ---
  # Title here
  Body...
The front matter is optional. If present, it extracts `labels` (array). The title is taken from the first H1 (# ...).
If no H1 exists, the filename (without extension) is used as the title.
The body sent to GitHub will be the markdown content WITHOUT the front matter and WITHOUT the first H1 line.
"""

import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import List, Optional, Tuple

FRONT_MATTER_DELIM = re.compile(r'^\s*---\s*$')
LABELS_LINE_RE = re.compile(r'^\s*labels\s*:\s*\[(.*?)\]\s*$', re.IGNORECASE)

def parse_front_matter_and_body(text: str) -> Tuple[Optional[List[str]], str]:
    """
    Returns (labels, body_without_front_matter)
    labels: list[str] or None
    """
    lines = text.splitlines()
    labels = None

    if lines and FRONT_MATTER_DELIM.match(lines[0]):
        # Find closing ---
        end_idx = None
        for i in range(1, len(lines)):
            if FRONT_MATTER_DELIM.match(lines[i]):
                end_idx = i
                break
        if end_idx is not None:
            # Parse lines[1:end_idx] looking for labels
            for fm_line in lines[1:end_idx]:
                m = LABELS_LINE_RE.match(fm_line.strip())
                if m:
                    raw = m.group(1).strip()
                    if raw:
                        # split by comma respecting simple quotes or double quotes
                        parts = [p.strip().strip("'\"") for p in raw.split(",")]
                        labels = [p for p in parts if p]
            # Body is remainder after end_idx
            body = "\n".join(lines[end_idx+1:]).lstrip("\n")
            return labels, body

    # No (valid) front matter; return original text as body
    return labels, text

H1_RE = re.compile(r'^\s*#\s+(.*)$')

def extract_title_and_body(body: str, fallback_title: str) -> Tuple[str, str]:
    """
    Finds first H1 (# ...) as title; removes that line from the body.
    If not found, uses fallback_title.
    """
    lines = body.splitlines()
    for i, line in enumerate(lines):
        m = H1_RE.match(line)
        if m:
            title = m.group(1).strip()
            # drop this line from body
            new_body = "\n".join(lines[:i] + lines[i+1:]).lstrip("\n")
            return title, new_body
    # No H1 found
    return fallback_title, body

def ensure_in_git_repo(repo_root: Path) -> None:
    # Quick sanity check: is this a Git repo root (or within it)?
    try:
        subprocess.run(["git", "-C", str(repo_root), "rev-parse", "--is-inside-work-tree"],
                       check=True, capture_output=True, text=True)
    except subprocess.CalledProcessError:
        print(f"[ERROR] {repo_root} non sembra una repository Git valida.", file=sys.stderr)
        sys.exit(2)

def gh_auth_ok() -> bool:
    try:
        out = subprocess.run(["gh", "auth", "status"], capture_output=True, text=True, check=True)
        return True
    except Exception:
        return False

def issue_exists_by_title(title: str) -> bool:
    """
    Return True if an open issue with the same title already exists.
    """
    try:
        # Use --state all to avoid duplicating closed issues (you may prefer only 'open')
        res = subprocess.run(
            ["gh", "issue", "list", "--search", f'in:title "{title}"', "--state", "all", "--json", "title"],
            capture_output=True, text=True, check=True
        )
        # Very simple check: look for exact match in returned JSON
        return f'"title":"{title}"' in res.stdout.replace(" ", "")
    except subprocess.CalledProcessError:
        # If list fails, assume not existing to avoid blocking
        return False

def create_issue(title: str, labels: Optional[List[str]], body_markdown: str, dry_run: bool) -> None:
    # Write body to a temp file
    with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False, encoding="utf-8") as tf:
        tf.write(body_markdown)
        tf_path = tf.name

    try:
        args = ["gh", "issue", "create", "--title", title, "--body-file", tf_path]
        if labels:
            for lb in labels:
                args.extend(["--label", lb])

        if dry_run:
            print("[DRY-RUN] gh cmd:", " ".join(f'"{a}"' if " " in a else a for a in args))
        else:
            subprocess.run(args, check=True)
            print(f"[OK] Created issue: {title}")
    finally:
        try:
            os.remove(tf_path)
        except OSError:
            pass

def main():
    parser = argparse.ArgumentParser(description="Create GitHub issues from Markdown files using gh.")
    parser.add_argument("folder", help="Path alla cartella con i file .md (ognuno diventa un'issue).")
    parser.add_argument("--repo-root", help="Root della repo Git (default: cwd).", default=os.getcwd())
    parser.add_argument("--glob", help="Pattern glob per i file (default: *.md).", default="*.md")
    parser.add_argument("--skip-existing", action="store_true", help="Salta la creazione se un'issue con lo stesso titolo esiste già.")
    parser.add_argument("--dry-run", action="store_true", help="Non crea davvero le issue; stampa solo i comandi gh.")
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()
    ensure_in_git_repo(repo_root)

    if not gh_auth_ok():
        print("[ERROR] GitHub CLI non autenticata. Esegui prima: gh auth login", file=sys.stderr)
        sys.exit(3)

    folder = Path(args.folder).resolve()
    if not folder.exists() or not folder.is_dir():
        print(f"[ERROR] Cartella non trovata: {folder}", file=sys.stderr)
        sys.exit(4)

    files = sorted(folder.glob(args.glob))
    if not files:
        print(f"[INFO] Nessun file trovato in {folder} con pattern {args.glob}")
        return

    for f in files:
        try:
            text = f.read_text(encoding="utf-8")
        except Exception as e:
            print(f"[WARN] Impossibile leggere {f}: {e}")
            continue

        labels, body_wo_fm = parse_front_matter_and_body(text)
        title, body = extract_title_and_body(body_wo_fm, fallback_title=f.stem)

        if args.skip_existing and issue_exists_by_title(title):
            print(f"[SKIP] Esiste già un'issue con il titolo: {title}")
            continue

        create_issue(title=title, labels=labels, body_markdown=body, dry_run=args.dry_run)

if __name__ == "__main__":
    main()
