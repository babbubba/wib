#!/usr/bin/env python3
"""
Converti export Doccano (JSON o JSONL) in payload per /train del servizio ML.

Output conforme a services/ml/main.py:
{
  "examples": [
    {"labelRaw": "...", "brand": null, "finalTypeId": "...", "finalCategoryId": "..."},
    ...
  ]
}

Assunzioni & mapping di default:
- Doccano classification: campo `text` come labelRaw.
- `labels`: se lista non vuota → labels[0] = finalTypeId; se presente labels[1] → finalCategoryId.
- Se presenti meta fields `typeId`/`categoryId` → hanno precedenza.
- `brand` opzionale da meta.brand (se presente), altrimenti None.

Opzioni CLI per casi diversi:
- --type-key / --category-key per leggere type/category da meta con chiavi custom.
- --type-index / --category-index per scegliere indici da `labels` (default 0 e 1).
- --brand-key per specificare la chiave di brand in meta (default `brand`).
- --post URL per inviare direttamente il payload a /train, altrimenti salva su file.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional


def read_records(path: Path) -> Iterable[Dict[str, Any]]:
    text = path.read_text(encoding="utf-8").strip()
    if not text:
        return []
    # JSON array export
    if text.startswith("["):
        try:
            data = json.loads(text)
            if isinstance(data, list):
                for obj in data:
                    if isinstance(obj, dict):
                        yield obj
                return
        except Exception:
            pass
    # JSONL export
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
            if isinstance(obj, dict):
                yield obj
        except Exception:
            continue


def pick_meta(meta: Dict[str, Any], key: Optional[str]) -> Optional[str]:
    if not key:
        return None
    if not isinstance(meta, dict):
        return None
    v = meta.get(key)
    if v is None:
        return None
    return str(v)


def pick_from_labels(labels: Any, index: Optional[int]) -> Optional[str]:
    if index is None:
        return None
    if not isinstance(labels, list) or not labels:
        return None
    if index < 0 or index >= len(labels):
        return None
    v = labels[index]
    # Doccano a volte esporta tuple o numeri; convertili a stringa
    if isinstance(v, (list, tuple)) and v:
        v = v[0]
    return str(v)


def build_examples(
    records: Iterable[Dict[str, Any]],
    *,
    type_key: Optional[str],
    category_key: Optional[str],
    brand_key: Optional[str],
    type_index: int,
    category_index: Optional[int],
) -> List[Dict[str, Any]]:
    examples: List[Dict[str, Any]] = []
    for r in records:
        text = r.get("text")
        if not isinstance(text, str) or not text.strip():
            # Salta record senza testo
            continue
        meta = r.get("meta") or r.get("metadata") or {}
        labels = r.get("labels")

        final_type = pick_meta(meta, type_key) or pick_from_labels(labels, type_index)
        final_cat = pick_meta(meta, category_key) or pick_from_labels(labels, category_index)
        brand = pick_meta(meta, brand_key)

        if not final_type:
            # Per /train finalTypeId è richiesto: se assente, salta il record
            continue

        examples.append(
            {
                "labelRaw": text.strip(),
                "brand": brand,
                "finalTypeId": str(final_type),
                "finalCategoryId": (str(final_cat) if final_cat is not None else None),
            }
        )
    return examples


def maybe_post(url: str, payload: Dict[str, Any]) -> None:
    try:
        import urllib.request
        import urllib.error

        req = urllib.request.Request(
            url,
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req) as resp:  # nosec B310
            body = resp.read().decode("utf-8", errors="ignore")
            print(f"[✓] POST {url} -> {resp.status}: {body[:200]}")
    except Exception as e:
        print(f"[!] POST fallita: {e}")


def main() -> None:
    ap = argparse.ArgumentParser(description="Doccano export -> payload /train ML")
    ap.add_argument("--input", type=Path, required=True, help="File export Doccano (JSON o JSONL)")
    ap.add_argument("--out", type=Path, default=Path("./.data/ml/train.json"), help="File JSON output")
    ap.add_argument("--type-key", default="typeId", help="Chiave in meta per finalTypeId (predef. typeId)")
    ap.add_argument("--category-key", default="categoryId", help="Chiave in meta per finalCategoryId (predef. categoryId)")
    ap.add_argument("--brand-key", default="brand", help="Chiave in meta per brand (predef. brand)")
    ap.add_argument("--type-index", type=int, default=0, help="Indice in labels per finalTypeId (fallback se meta mancante)")
    ap.add_argument("--category-index", type=int, default=1, help="Indice in labels per finalCategoryId (fallback se meta mancante; -1 per disabilitare)")
    ap.add_argument("--post", type=str, default=None, help="URL di /train per invio diretto (es. http://localhost:8082/train)")
    args = ap.parse_args()

    if args.category_index < 0:
        cat_idx = None
    else:
        cat_idx = args.category_index

    records = list(read_records(args.input))
    ex = build_examples(
        records,
        type_key=args.type_key,
        category_key=args.category_key,
        brand_key=args.brand_key,
        type_index=args.type_index,
        category_index=cat_idx,
    )
    payload = {"examples": ex}

    outp: Path = args.out
    outp.parent.mkdir(parents=True, exist_ok=True)
    outp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[✓] Scritto payload di {len(ex)} esempi in: {outp}")

    if args.post:
        maybe_post(args.post, payload)


if __name__ == "__main__":
    main()

