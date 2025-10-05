#!/usr/bin/env python3
"""
Convertitore dataset da Label Studio verso:
 - PP-Structure (SER/RE) JSONL con ocr_info/relations
 - Donut (JSONL con ground_truth schema ricevuta WIB)

Uso rapido:
  python docs/convert_dataset.py \
    --input ./labelstudio_export.json \
    --images-root ./images \
    --out-dir ./.data/datasets \
    --target ppstructure \
    --split 0.9 0.1 0 \
    --label-map docs/label_map.example.json

Nota: questo script applica euristiche ragionevoli per testi/etichette e non sostituisce
una definizione rigorosa del tuo schema Label Studio. Adatta la mappatura
in base al tuo progetto.
"""

from __future__ import annotations

import argparse
import json
import os
import random
import re
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

try:
    from PIL import Image  # type: ignore
except Exception:  # pragma: no cover - opzionale
    Image = None  # type: ignore


def _ensure_dir(p: Path) -> None:
    p.mkdir(parents=True, exist_ok=True)


def _to_abs(p: Path) -> Path:
    return p if p.is_absolute() else p.resolve()


def _parse_floatsafe(s: Optional[str]) -> Optional[float]:
    if s is None:
        return None
    s = s.strip()
    if not s:
        return None
    # accetta virgola come separatore decimale
    s = s.replace(" ", "").replace(",", ".")
    try:
        return float(s)
    except Exception:
        # prova a estrarre la prima cifra
        m = re.search(r"[-+]?[0-9]*\.?[0-9]+", s)
        if m:
            try:
                return float(m.group(0))
            except Exception:
                return None
        return None


def _norm_label(x: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", x, flags=re.I).lower()


DEFAULT_LABEL_MAP: Dict[str, List[str]] = {
    # campi chiave
    "store": ["store", "shop", "market", "insegna"],
    "datetime": ["datetime", "date", "data", "timestamp"],
    "currency": ["currency", "valuta"],
    "subtotal": ["subtotal", "imponibile"],
    "tax": ["tax", "iva", "vat"],
    "total": ["total", "totale"],
    # righe
    "line": ["line", "item", "row", "riga"],
}


@dataclass
class OcrBox:
    text: str
    bbox: Tuple[int, int, int, int]
    label: str
    score: Optional[float] = None


@dataclass
class Sample:
    image_src: Path
    image_name: str
    width: int
    height: int
    boxes: List[OcrBox] = field(default_factory=list)
    fields: Dict[str, Any] = field(default_factory=dict)  # store/datetime/currency/subtotal/tax/total


def load_label_map(path: Optional[Path]) -> Dict[str, List[str]]:
    if path and path.exists():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            normed = {k: [*{_norm_label(v) for v in vs}] for k, vs in data.items()}
            # mantieni chiavi note; ignora extra
            out = dict(DEFAULT_LABEL_MAP)
            out.update({k: v for k, v in normed.items() if k in out})
            return out
        except Exception:
            pass
    return DEFAULT_LABEL_MAP


def _guess_image_name(task: Dict[str, Any]) -> str:
    # Prova da data.image (può essere URL o path); fallback: id
    data = task.get("data") or {}
    for key in ("image", "img", "path"):
        v = data.get(key)
        if isinstance(v, str) and v:
            return Path(v).name
    # Prova da file_upload di Label Studio
    if "file_upload" in task:
        return Path(str(task["file_upload"])).name
    # fallback su id
    return f"task_{task.get('id', 'unknown')}.jpg"


def _abs_bbox(value: Dict[str, Any], iw: int, ih: int) -> Tuple[int, int, int, int]:
    # Label Studio rectangle labels in percent
    x = value.get("x", 0) / 100.0 * iw
    y = value.get("y", 0) / 100.0 * ih
    w = value.get("width", 0) / 100.0 * iw
    h = value.get("height", 0) / 100.0 * ih
    x1 = int(round(max(0, x)))
    y1 = int(round(max(0, y)))
    x2 = int(round(min(iw - 1, x + w)))
    y2 = int(round(min(ih - 1, y + h)))
    return x1, y1, x2, y2


def parse_labelstudio(ls_path: Path, images_root: Path) -> List[Sample]:
    tasks = json.loads(ls_path.read_text(encoding="utf-8"))
    out: List[Sample] = []
    for t in tasks:
        img_name = _guess_image_name(t)
        img_src = images_root / img_name
        iw = None
        ih = None

        # risultati
        annos = t.get("annotations") or t.get("completions") or []
        results: List[Dict[str, Any]] = []
        if annos:
            # prendi la prima annotazione completata
            ann = annos[0]
            results = ann.get("result", [])

        # deduci dimensioni
        for r in results:
            v = r.get("value", {})
            ow = v.get("original_width")
            oh = v.get("original_height")
            if isinstance(ow, (int, float)) and isinstance(oh, (int, float)):
                iw = int(ow)
                ih = int(oh)
                break

        if (iw is None or ih is None) and Image is not None and img_src.exists():
            try:
                with Image.open(img_src) as im:
                    iw, ih = im.size
            except Exception:
                pass

        if iw is None or ih is None:
            # fallback generico se non possiamo leggere dimensioni
            iw, ih = 1000, 1500

        sample = Sample(image_src=img_src, image_name=img_name, width=iw, height=ih)

        # campi testuali (textarea/choices): raccogli in mappa fields
        for r in results:
            rtype = r.get("type") or r.get("type_id")
            v = r.get("value", {})
            from_name = _norm_label(str(r.get("from_name", "")))
            if rtype in ("textarea", "choices", "labels", "text"):
                txt = None
                if isinstance(v, dict):
                    if "text" in v and isinstance(v["text"], list) and v["text"]:
                        txt = str(v["text"][0])
                    elif isinstance(v.get("choices"), list) and v["choices"]:
                        txt = str(v["choices"][0])
                if txt:
                    sample.fields[from_name] = txt

        # box OCR / linee
        for r in results:
            v = r.get("value", {})
            rtype = r.get("type") or r.get("type_id")
            if rtype not in ("rectanglelabels", "keypointlabels", "brushlabels"):
                continue
            labels = v.get("rectanglelabels") or v.get("labels") or []
            label = str(labels[0]) if labels else "O"
            text = None
            if isinstance(v.get("text"), list) and v["text"]:
                text = str(v["text"][0])
            bbox = _abs_bbox(v, iw, ih)
            sample.boxes.append(OcrBox(text=text or "", bbox=bbox, label=label))

        out.append(sample)
    return out


def split_samples(samples: List[Sample], split: Tuple[float, float, float], seed: int) -> Dict[str, List[Sample]]:
    assert abs(sum(split) - 1.0) < 1e-6, "Lo split deve sommare a 1.0"
    rnd = random.Random(seed)
    items = samples[:]
    rnd.shuffle(items)
    n = len(items)
    n_train = int(round(split[0] * n))
    n_val = int(round(split[1] * n))
    train = items[:n_train]
    val = items[n_train:n_train + n_val]
    test = items[n_train + n_val:]
    return {"train": train, "val": val, "test": test}


def write_ppstructure(split_data: Dict[str, List[Sample]], out_root: Path) -> None:
    base = out_root / "ppstructure"
    for part, items in split_data.items():
        if not items:
            continue
        out_dir = base / part
        img_dir = out_dir / "images"
        _ensure_dir(img_dir)
        jsonl_path = out_dir / "data.jsonl"
        with jsonl_path.open("w", encoding="utf-8") as f:
            for s in items:
                # copia immagine se esiste
                try:
                    if s.image_src.exists():
                        shutil.copy2(s.image_src, img_dir / s.image_name)
                except Exception:
                    pass
                record = {
                    "img_path": str(Path("images") / s.image_name),
                    "ocr_info": [
                        {
                            "text": b.text,
                            "bbox": list(b.bbox),
                            "label": b.label.upper(),
                        }
                        for b in s.boxes
                    ],
                    "relations": [],  # opzionale, non derivato da LS per default
                }
                f.write(json.dumps(record, ensure_ascii=False) + "\n")


def _field_from_aliases(fields: Dict[str, Any], aliases: List[str]) -> Optional[str]:
    for a in aliases:
        if a in fields and isinstance(fields[a], str) and fields[a].strip():
            return fields[a].strip()
    return None


def write_donut(split_data: Dict[str, List[Sample]], out_root: Path, label_map: Dict[str, List[str]]) -> None:
    base = out_root / "donut"
    for part, items in split_data.items():
        if not items:
            continue
        out_dir = base / part
        img_dir = out_dir / "images"
        _ensure_dir(img_dir)
        jsonl_path = out_dir / "data.jsonl"
        with jsonl_path.open("w", encoding="utf-8") as f:
            for s in items:
                try:
                    if s.image_src.exists():
                        shutil.copy2(s.image_src, img_dir / s.image_name)
                except Exception:
                    pass

                store_name = _field_from_aliases(s.fields, [_norm_label(x) for x in label_map.get("store", [])]) or ""
                dt = _field_from_aliases(s.fields, [_norm_label(x) for x in label_map.get("datetime", [])]) or ""
                currency = _field_from_aliases(s.fields, [_norm_label(x) for x in label_map.get("currency", [])]) or ""
                subtotal = _parse_floatsafe(
                    _field_from_aliases(s.fields, [_norm_label(x) for x in label_map.get("subtotal", [])])
                ) or 0.0
                tax = _parse_floatsafe(
                    _field_from_aliases(s.fields, [_norm_label(x) for x in label_map.get("tax", [])])
                ) or 0.0
                total = _parse_floatsafe(
                    _field_from_aliases(s.fields, [_norm_label(x) for x in label_map.get("total", [])])
                ) or 0.0

                # linee: preferisci box con label di tipo "line"; altrimenti usa tutti i box come labelRaw
                line_aliases = set([_norm_label(x) for x in label_map.get("line", [])])
                lines = []
                candidates = [b for b in s.boxes if _norm_label(b.label) in line_aliases]
                if not candidates:
                    candidates = s.boxes
                for b in candidates:
                    lines.append(
                        {
                            "labelRaw": b.text or "",
                            "qty": 1,
                            "unitPrice": 0.0,
                            "lineTotal": 0.0,
                            "vatRate": None,
                        }
                    )

                gt = {
                    "store": {"name": store_name, "address": None, "city": None, "chain": None},
                    "datetime": dt,
                    "currency": currency or "",
                    "lines": lines,
                    "totals": {"subtotal": subtotal, "tax": tax, "total": total},
                }

                record = {
                    "image": str(Path("images") / s.image_name),
                    "ground_truth": json.dumps(gt, ensure_ascii=False),
                }
                f.write(json.dumps(record, ensure_ascii=False) + "\n")


def main() -> None:
    ap = argparse.ArgumentParser(description="Converti Label Studio verso PP-Structure/Donut")
    ap.add_argument("--input", type=Path, required=True, help="Export JSON Label Studio")
    ap.add_argument("--images-root", type=Path, required=True, help="Cartella dove risiedono le immagini")
    ap.add_argument("--out-dir", type=Path, required=True, help="Cartella di output")
    ap.add_argument("--target", choices=["ppstructure", "donut"], required=True)
    ap.add_argument("--split", nargs=3, type=float, default=[0.9, 0.1, 0.0], metavar=("TRAIN", "VAL", "TEST"))
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--label-map", type=Path, default=None, help="JSON con alias etichette (vedi esempio)")
    args = ap.parse_args()

    images_root = _to_abs(args.images_root)
    out_root = _to_abs(args.out_dir)
    _ensure_dir(out_root)

    label_map = load_label_map(args.label_map)

    print(f"[i] Carico export: {args.input}")
    samples = parse_labelstudio(_to_abs(args.input), images_root)
    if not samples:
        print("[!] Nessun campione trovato. Controlla il file di export.")
        return
    print(f"[i] Trovati campioni: {len(samples)}")

    sp = (args.split[0], args.split[1], args.split[2])
    try:
        split_data = split_samples(samples, sp, args.seed)
    except AssertionError as e:
        print(f"[!] Split non valido: {e}")
        return

    if args.target == "ppstructure":
        write_ppstructure(split_data, out_root)
        print(f"[✓] Dataset PP-Structure scritto in: {out_root / 'ppstructure'}")
    else:
        write_donut(split_data, out_root, label_map)
        print(f"[✓] Dataset Donut scritto in: {out_root / 'donut'}")


if __name__ == "__main__":
    main()

