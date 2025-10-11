import os
import base64
from fastapi import FastAPI, UploadFile, File
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from typing import List, Optional, Tuple

app = FastAPI()

OCR_STUB_ENABLED = os.getenv("OCR_STUB", "true").lower() == "true"
OCR_STUB_TEXT = os.getenv("OCR_STUB_TEXT", "mock-ocr")

# --- KIE engine wiring (PP-Structure / Donut) ---

class KieEngine:
    def __init__(self):
        self.kind: str = "stub"  # one of: stub|ppstructure|donut
        self.ready: bool = False
        self.detail: str = ""
        self.model_dir: Optional[str] = None
        self.extra: dict = {}

    def load(self):
        self.model_dir = os.getenv("KIE_MODEL_DIR")
        ser_cfg = os.getenv("PP_STRUCTURE_SER_CFG")
        re_cfg = os.getenv("PP_STRUCTURE_RE_CFG")
        donut_ckpt = os.getenv("DONUT_CHECKPOINT")

        # Prefer Donut if checkpoint is provided
        if donut_ckpt and self.model_dir:
            try:
                # Deferred import; optional dependency
                from importlib import import_module  # noqa: F401
                # If transformers isn't installed, this will raise when used.
                self.kind = "donut"
                self.ready = os.path.exists(donut_ckpt)
                self.detail = "Donut checkpoint detected" if self.ready else "Donut checkpoint missing"
                self.extra = {"checkpoint": donut_ckpt}
                return
            except Exception as e:
                self.kind = "stub"
                self.ready = False
                self.detail = f"Donut not available: {e}"

        # Otherwise try PP-Structure
        if (ser_cfg or re_cfg) and self.model_dir:
            try:
                # Optional import; the service can still run without it
                import importlib
                importlib.import_module("paddleocr")
                self.kind = "ppstructure"
                self.ready = all(
                    [
                        (ser_cfg is None or os.path.exists(ser_cfg)),
                        (re_cfg is None or os.path.exists(re_cfg)),
                    ]
                )
                self.detail = (
                    "PP-Structure configs detected"
                    if self.ready
                    else "PP-Structure config paths missing"
                )
                self.extra = {"ser_cfg": ser_cfg, "re_cfg": re_cfg}
                return
            except Exception as e:
                self.kind = "stub"
                self.ready = False
                self.detail = f"PP-Structure not available: {e}"

        # Fallback: stub
        self.kind = "stub"
        self.ready = False
        if not self.detail:
            self.detail = "No KIE model configured; using stub"

    def infer_image(self, image_bytes: bytes) -> dict:
        text = ocr_text(image_bytes)
        return parse_text(text)


KIE = KieEngine()
KIE.load()

@app.get("/health")
def health():
    return {"status": "ok"}

@app.post("/extract")
async def extract(file: UploadFile = File(...)):
    data = await file.read()
    if OCR_STUB_ENABLED:
        return JSONResponse({"text": OCR_STUB_TEXT})
    if not data:
        return JSONResponse({"text": ""})
    text = ocr_text(data)
    if not text.strip():
        if OCR_STUB_ENABLED and OCR_STUB_TEXT:
            return JSONResponse({"text": OCR_STUB_TEXT})
        return JSONResponse({"text": ""})
    return JSONResponse({"text": text})


class KieRequest(BaseModel):
    text: str
    # Opzionale: immagine codificata base64 per usare un KIE reale se configurato
    image_b64: Optional[str] = None

class KieStore(BaseModel):
    name: str
    address: Optional[str] = None
    city: Optional[str] = None
    chain: Optional[str] = None
    postalCode: Optional[str] = None
    vatNumber: Optional[str] = None

class KieLine(BaseModel):
    labelRaw: str
    qty: float
    unitPrice: float
    lineTotal: float
    vatRate: Optional[float] = None

class KieTotals(BaseModel):
    subtotal: float
    tax: float
    total: float

class KieResponse(BaseModel):
    store: KieStore
    datetime: str
    currency: str
    lines: List[KieLine]
    totals: KieTotals


@app.post("/kie")
async def kie(req: KieRequest):
    # Se disponibile, e se viene fornita un'immagine, usa il motore KIE configurato
    if req.image_b64 and KIE.kind != "stub" and KIE.ready:
        try:
            img_bytes = base64.b64decode(req.image_b64)
            pred = KIE.infer_image(img_bytes)
            return JSONResponse(pred)
        except Exception:
            # Fallback a stub se l'inferenza fallisce
            pass

    # Heuristic parsing based on OCR text
    try:
        pred = parse_text(req.text or "")
        return JSONResponse(pred)
    except Exception:
        return JSONResponse(KieResponse(
            store=KieStore(name=""),
            datetime="",
            currency="EUR",
            lines=[],
            totals=KieTotals(subtotal=0.0, tax=0.0, total=0.0),
        ).model_dump())


@app.get("/kie/status")
def kie_status():
    return {
        "engine": KIE.kind,
        "ready": KIE.ready,
        "detail": KIE.detail,
        "model_dir": KIE.model_dir,
        "extra": KIE.extra,
    }

# --- Simple OCR and parsing pipeline (Tesseract + heuristics) ---

import io
import re
from datetime import datetime, timezone
from dateutil import parser as dateparser
from PIL import Image, ImageOps, ImageFilter
import pytesseract


def ocr_text(image_bytes: bytes) -> str:
    try:
        with Image.open(io.BytesIO(image_bytes)) as img:
            img = img.convert("L")
            img = ImageOps.autocontrast(img)
            img = img.filter(ImageFilter.SHARPEN)
            # Try OpenCV threshold/denoise if available
            try:
                import numpy as np  # type: ignore
                import cv2  # type: ignore
                arr = np.array(img)
                arr = cv2.fastNlMeansDenoising(arr, h=10)
                thr = cv2.adaptiveThreshold(arr, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
                                            cv2.THRESH_BINARY, 31, 10)
                img = Image.fromarray(thr)
            except Exception:
                pass
            text = pytesseract.image_to_string(img, config="--oem 3 --psm 6")
            return text
    except Exception:
        return ""


def parse_text(text: str) -> dict:
    lines = [clean_line(l) for l in text.splitlines()]
    lines = [l for l in lines if l]

    store_name = infer_store(lines)
    address, city, cap = infer_address_city_cap(lines)
    vat = infer_vat(lines)
    dt_iso = infer_datetime(lines) or datetime.now(timezone.utc).isoformat()
    currency = infer_currency(text)
    items = infer_items(lines)
    totals = infer_totals(lines, items)

    return {
        "store": {"name": store_name or "", "address": address, "city": city, "postalCode": cap, "vatNumber": vat},
        "datetime": dt_iso,
        "currency": currency or "EUR",
        "lines": [
            {
                "labelRaw": it["label"],
                "qty": float(it["qty"]),
                "unitPrice": float(it["unit"]),
                "lineTotal": float(it["total"]),
                "vatRate": float(it["vat"]) if it.get("vat") is not None else None,
            }
            for it in items
        ],
        "totals": {
            "subtotal": float(totals.get("subtotal", 0.0)),
            "tax": float(totals.get("tax", 0.0)),
            "total": float(totals.get("total", sum(it["total"] for it in items))),
        },
    }


def clean_line(s: str) -> str:
    s = s.strip()
    # Replace weird unicode spaces and normalize commas
    s = re.sub(r"\s+", " ", s)
    return s


def infer_store(lines: list[str]) -> Optional[str]:
    # Heuristic: first line with >3 letters and few digits
    for l in lines[:6]:
        letters = len(re.findall(r"[A-Za-z]", l))
        digits = len(re.findall(r"\d", l))
        if letters >= 3 and digits <= 2 and len(l) >= 3:
            return l
    return lines[0] if lines else None


def infer_address_city_cap(lines: list[str]) -> Tuple[Optional[str], Optional[str], Optional[str]]:
    # Look in the top ~12 lines for address-like strings and CAP (5 digits)
    addr = None
    city = None
    cap = None
    top = lines[:12]
    street_kw = re.compile(r"\b(via|viale|v\.?le|piazza|p\.?za|p\.?zza|corso|cso\.?|largo|lgo\.|strada|str\.)\b", re.I)
    cap_re = re.compile(r"\b(\d{5})\b")
    for l in top:
        if cap is None:
            mcap = cap_re.search(l)
            if mcap:
                cap = mcap.group(1)
        if addr is None and street_kw.search(l):
            addr = l
        # Heuristic: CITY often in uppercase near CAP
        if city is None and cap and cap in l:
            # Try to extract words after CAP
            parts = l.split(cap)
            tail = parts[-1].strip()
            mcity = re.search(r"([A-ZÀ-Ü][A-ZÀ-Ü\s'\-]{2,})", tail)
            if mcity:
                city = mcity.group(1).title()
    return addr, city, cap


def infer_vat(lines: list[str]) -> Optional[str]:
    # Italian VAT: 11 digits, often labeled P.IVA/Partita IVA/VAT
    vat_re = re.compile(r"(p\.?\s?iva|partita\s+iva|vat)[^\d]*(\d{11})", re.I)
    for l in lines[:15]:
        m = vat_re.search(l)
        if m:
            return m.group(2)
    # Fallback: search anywhere
    for l in lines:
        m = vat_re.search(l)
        if m:
            return m.group(2)
    return None


def parse_number(s: str) -> Optional[float]:
    s = s.strip()
    # Convert common European decimals like 1,23 to 1.23
    s = s.replace("€", "").replace("", "")
    # If both comma and dot, assume comma is thousand sep
    if "," in s and "." in s:
        if s.rfind(",") > s.rfind("."):
            s = s.replace(".", "").replace(",", ".")
    elif "," in s and "." not in s:
        s = s.replace(",", ".")
    try:
        return float(re.findall(r"-?\d+(?:\.\d+)?", s)[-1])
    except Exception:
        return None


def infer_datetime(lines: list[str]) -> Optional[str]:
    text = " ".join(lines)
    # Common date patterns DD/MM/YYYY, DD-MM-YYYY, etc
    m = re.search(r"(\d{1,2}[\./-]\d{1,2}[\./-]\d{2,4})(?:\s+(\d{1,2}:\d{2}(?::\d{2})?\s?(?:AM|PM|am|pm)?))?", text)
    if m:
        dt_str = m.group(0)
        try:
            dt = dateparser.parse(dt_str, dayfirst=True, fuzzy=True)
            return dt.astimezone().isoformat()
        except Exception:
            pass
    # Fallback: none
    return None


def infer_currency(text: str) -> str:
    if "€" in text or re.search(r"\bEUR\b", text, re.I):
        return "EUR"
    if re.search(r"\bUSD\b|\$", text):
        return "USD"
    if re.search(r"\bGBP\b|£", text):
        return "GBP"
    return "EUR"


def infer_items(lines: list[str]) -> list[dict]:
    items: list[dict] = []
    price_re = re.compile(r"(?P<val>-?\d+[\.,]\d{2})")
    qty_x_price_re = re.compile(r"(?P<label>.+?)\s+(?P<qty>\d+(?:[\.,]\d+)?)\s*[xX]\s*(?P<Unit>-?\d+[\.,]\d{2})\s+(?P<tot>-?\d+[\.,]\d{2})(?:.*?(?P<vat>\d{1,2}(?:[\.,]\d+)?%))?$")
    label_price_re = re.compile(r"(?P<label>.+?)\s+(?P<tot>-?\d+[\.,]\d{2})(?:.*?(?P<vat>\d{1,2}(?:[\.,]\d+)?%))?$")
    blacklist_re = re.compile(r"\b(TOTALE\s+COMPLESSIVO|TOTALE\s+EURO|TOTALE|SUBTOTALE|PAGAMENTO|RESTO|SCONTO|BANCOMAT|CARTA\s+DI\s+CREDITO|CONTANTI|ARTICOLI|IMPORTO|CASSA)\b", re.I)
    for l in lines:
        l2 = l
        # skip obvious non-product headers (totali/pagamenti)
        if re.search(r"TOTALE|TOTAL|SUBTOTAL|PAGAMENTO|CONTANTE|CONTANTI|IMPORTO PAGATO|RESTO", l2, re.I) or blacklist_re.search(l2):
            continue
        m = qty_x_price_re.search(l2)
        if m:
            qty = parse_number(m.group("qty")) or 1.0
            unit = parse_number(m.group("Unit")) or 0.0
            tot = parse_number(m.group("tot")) or (qty * unit)
            label = m.group("label").strip()
            vat = None
            if m.group("vat"):
                vat = parse_number(m.group("vat").replace('%',''))
            if tot is not None:
                items.append({"label": label, "qty": qty, "unit": unit or (tot / max(qty, 1)), "total": tot, "vat": vat})
            continue
        # Label with single price at end
        m2 = label_price_re.search(l2)
        if m2:
            tot = parse_number(m2.group("tot"))
            label = m2.group("label").strip()
            vat = None
            if m2.group("vat"):
                vat = parse_number(m2.group("vat").replace('%',''))
            if tot is not None:
                items.append({"label": label, "qty": 1.0, "unit": tot, "total": tot, "vat": vat})
                continue
        # As a fallback, try to split by the last price occurrence
        prices = list(price_re.finditer(l2))
        if prices:
            p = prices[-1]
            label = l2[: p.start()].strip()
            tot = parse_number(p.group("val"))
            if tot is not None and label:
                # try find vat percent anywhere in line
                mvat = re.search(r"(\d{1,2}(?:[\.,]\d+)?%)", l2)
                vat = parse_number(mvat.group(1).replace('%','')) if mvat else None
                items.append({"label": label, "qty": 1.0, "unit": tot, "total": tot, "vat": vat})
    return items


def infer_totals(lines: list[str], items: list[dict]) -> dict:
    totals = {"subtotal": 0.0, "tax": 0.0, "total": 0.0}
    text_lines = list(lines)
    # Search totals keywords
    for l in text_lines[::-1]:
        if re.search(r"TOTALE\s*\w*|TOTAL", l, re.I):
            val = parse_number(l)
            if val is not None:
                totals["total"] = val
                break
    for l in text_lines:
        if re.search(r"SUB\s*TOTAL|SUBTOTALE", l, re.I):
            val = parse_number(l)
            if val is not None:
                totals["subtotal"] = val
    for l in text_lines:
        if re.search(r"IVA|VAT|TAX", l, re.I):
            val = parse_number(l)
            if val is not None:
                totals["tax"] = val
    if not totals["subtotal"]:
        totals["subtotal"] = sum(it["total"] for it in items)
    if not totals["total"]:
        totals["total"] = totals["subtotal"] + totals["tax"]
    return totals
