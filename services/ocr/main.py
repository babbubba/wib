import os
import sys
import base64
from fastapi import FastAPI, UploadFile, File
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from typing import List, Optional, Tuple

# Add shared module to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
from shared.redis_logger import RedisLogger, LogSeverity

app = FastAPI()

OCR_STUB_ENABLED = os.getenv("OCR_STUB", "false").lower() == "true"
temp_stub_text = os.getenv("OCR_STUB_TEXT")
OCR_STUB_TEXT = temp_stub_text if temp_stub_text is not None else "mock-ocr"

# Select OCR engine: 'tesseract' (default) or 'paddle'
OCR_ENGINE = os.getenv("OCR_ENGINE", "tesseract").strip().lower()

# Initialize Redis logger
REDIS_URL = os.getenv("REDIS_URL", "redis://redis:6379")
LOG_STREAM_KEY = os.getenv("LOG_STREAM_KEY", "app_logs")
LOG_LEVEL = LogSeverity(os.getenv("LOG_LEVEL", "INFO").upper())
logger = RedisLogger("ocr", REDIS_URL, LOG_STREAM_KEY, min_log_level=LOG_LEVEL)

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
        # Prefer structured parsing with line boxes
        try:
            lines = ocr_lines(image_bytes)
            return parse_text_with_lines(lines)
        except Exception:
            text = ocr_text(image_bytes)
            return parse_text(text)


KIE = KieEngine()
KIE.load()

@app.get("/health")
def health():
    return {"status": "ok"}

@app.post("/extract")
async def extract(file: UploadFile = File(...)):
    try:
        await logger.info("OCR Request", f"Received OCR extraction request, file: {file.filename}", {"fileSize": file.size})

        data = await file.read()
        if OCR_STUB_ENABLED and OCR_STUB_TEXT:
            await logger.debug("OCR Stub", "Using stub OCR mode")
            return JSONResponse({"text": OCR_STUB_TEXT})
        if not data:
            await logger.warning("Empty Image", "Received empty image data")
            return JSONResponse({"text": ""})

        await logger.info("Preprocessing Image", f"Preprocessing image for OCR, size: {len(data)} bytes")
        text_value = ocr_text(data)

        if not text_value.strip():
            await logger.warning("No Text Extracted", "OCR returned empty text")
            if OCR_STUB_TEXT:
                return JSONResponse({"text": OCR_STUB_TEXT})
            return JSONResponse({"text": ""})

        await logger.info("OCR Complete", f"Text extracted successfully, length: {len(text_value)} chars", {"textLength": len(text_value)})
        return JSONResponse({"text": text_value})
    except Exception as e:
        await logger.error("OCR Error", f"Error during OCR extraction: {str(e)}", e)
        raise


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
    try:
        await logger.info("KIE Request", "Received KIE extraction request", {"textLength": len(req.text or ""), "hasImage": req.image_b64 is not None})

        # Se disponibile, e se viene fornita un'immagine, usa il motore KIE configurato
        if req.image_b64 and KIE.kind != "stub" and KIE.ready:
            try:
                await logger.info("KIE Model Inference", f"Using {KIE.kind} model for KIE extraction")
                img_bytes = base64.b64decode(req.image_b64)
                pred = KIE.infer_image(img_bytes)
                await logger.info("KIE Complete", "KIE extraction successful using trained model", {"lineCount": len(pred.get("lines", []))})
                return JSONResponse(pred)
            except Exception as e:
                await logger.warning("KIE Model Failed", f"Model inference failed, falling back to heuristic parsing: {str(e)}")
                # Fallback a stub se l'inferenza fallisce
                pass

        # Heuristic parsing based on OCR text
        try:
            await logger.info("KIE Heuristic", "Using heuristic parsing for KIE extraction")
            pred = parse_text(req.text or "")
            await logger.info("KIE Complete", "KIE extraction successful using heuristics", {"lineCount": len(pred.get("lines", []))})
            return JSONResponse(pred)
        except Exception as e:
            await logger.error("KIE Error", f"KIE extraction failed: {str(e)}", e)
            return JSONResponse(KieResponse(
                store=KieStore(name=""),
                datetime="",
                currency="EUR",
                lines=[],
                totals=KieTotals(subtotal=0.0, tax=0.0, total=0.0),
            ).model_dump())
    except Exception as e:
        await logger.error("KIE Endpoint Error", f"Unexpected error in KIE endpoint: {str(e)}", e)
        raise


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
from PIL import Image, ImageOps, ImageFilter, ImageEnhance
import pytesseract
from .preprocessing import preprocess_image


def _get_tesseract_params() -> tuple[str, str]:
    lang = os.getenv("TESSERACT_LANG", "ita+eng").strip() or "ita+eng"
    def _to_int(name: str, default: int) -> int:
        try:
            v = int(os.getenv(name, str(default)))
            return v
        except Exception:
            return default
    psm = _to_int("TESSERACT_PSM", 6)
    oem = _to_int("TESSERACT_OEM", 3)
    cfg = f"--oem {oem} --psm {psm}"
    return lang, cfg


def ocr_text(image_bytes: bytes) -> str:
    try:
        if OCR_ENGINE == "paddle":
            lines = ocr_lines(image_bytes)
            return "\n".join([l.get("text", "") for l in lines])

        img = preprocess_image(image_bytes)
        lang, tess_cfg = _get_tesseract_params()
        # Prefer structured output to preserve visual row order
        try:
            data = pytesseract.image_to_data(
                img, output_type=pytesseract.Output.DICT, config=tess_cfg, lang=lang
            )
            n = len(data.get("text", []))
            rows: dict[tuple[int, int, int], dict] = {}
            for i in range(n):
                txt = (data["text"][i] or "").strip()
                if not txt:
                    continue
                key = (data.get("block_num", [0])[i], data.get("par_num", [0])[i], data.get("line_num", [0])[i])
                entry = rows.get(key)
                if entry is None:
                    entry = {
                        "top": int(data.get("top", [0])[i] or 0),
                        "left": int(data.get("left", [0])[i] or 0),
                        "words": [],
                    }
                    rows[key] = entry
                entry["top"] = min(entry["top"], int(data.get("top", [0])[i] or 0))
                entry["words"].append((int(data.get("left", [0])[i] or 0), txt))
            if rows:
                ordered = sorted(rows.values(), key=lambda r: r["top"])  # top-to-bottom
                lines_out: list[str] = []
                for r in ordered:
                    words = sorted(r["words"], key=lambda w: w[0])  # left-to-right
                    line = " ".join(w for _, w in words)
                    line = clean_line(line)
                    if line:
                        lines_out.append(line)
                return "\n".join(lines_out)
        except Exception:
            # Fallback to plain text if structured data fails
            pass

        text = pytesseract.image_to_string(img, config=tess_cfg, lang=lang)
        return text
    except Exception:
        return ""

def ocr_lines(image_bytes: bytes) -> list[dict]:
    if OCR_ENGINE == "paddle":
        try:
            from paddleocr import PaddleOCR  # type: ignore
            img = preprocess_image(image_bytes)
            import numpy as np  # type: ignore
            arr = np.array(img)
            ocr = PaddleOCR(use_angle_cls=True, lang='en', show_log=False)
            result = ocr.ocr(arr, cls=True)
            lines: list[dict] = []
            for block in result:
                for line in block:
                    box, (text, conf) = line
                    xs = [pt[0] for pt in box]
                    ys = [pt[1] for pt in box]
                    x1, y1 = int(min(xs)), int(min(ys))
                    x2, y2 = int(max(xs)), int(max(ys))
                    t = clean_line(text)
                    if t:
                        lines.append({'text': t, 'bbox': {'x': x1, 'y': y1, 'w': x2 - x1, 'h': y2 - y1}})
            # Sort top-to-bottom
            lines.sort(key=lambda l: l['bbox']['y'])
            return lines
        except Exception:
            # Fallback to tesseract
            pass

    from pytesseract import Output
    img = preprocess_image(image_bytes)
    lang, tess_cfg = _get_tesseract_params()
    data = pytesseract.image_to_data(img, output_type=Output.DICT, config=tess_cfg, lang=lang)
    n = len(data['text'])
    groups = {}
    order = []
    for i in range(n):
        txt = (data['text'][i] or '').strip()
        if not txt:
            continue
        key = (data['block_num'][i], data['par_num'][i], data['line_num'][i])
        if key not in groups:
            groups[key] = {
                'text_parts': [],
                'x1': 1e9, 'y1': 1e9, 'x2': -1, 'y2': -1
            }
            order.append(key)
        g = groups[key]
        g['text_parts'].append(txt)
        x, y, w, h = data['left'][i], data['top'][i], data['width'][i], data['height'][i]
        g['x1'] = min(g['x1'], x)
        g['y1'] = min(g['y1'], y)
        g['x2'] = max(g['x2'], x + w)
        g['y2'] = max(g['y2'], y + h)
    lines: list[dict] = []
    for key in order:
        g = groups[key]
        text = " ".join(g['text_parts']).strip()
        if not text:
            continue
        x1, y1, x2, y2 = g['x1'], g['y1'], g['x2'], g['y2']
        lines.append({'text': text, 'bbox': {'x': int(x1), 'y': int(y1), 'w': int(x2 - x1), 'h': int(y2 - y1)}})
    return lines


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
                "weightKg": float(it["weightKg"]) if it.get("weightKg") is not None else None,
                "pricePerKg": float(it["pricePerKg"]) if it.get("pricePerKg") is not None else None,
            }
            for it in items
        ],
        "totals": {
            "subtotal": float(totals.get("subtotal", 0.0)),
            "tax": float(totals.get("tax", 0.0)),
            "total": float(totals.get("total", sum(it["total"] for it in items))),
        },
    }

def parse_text_with_lines(lines_with_boxes: list[dict]) -> dict:
    # lines_with_boxes: [{text: str, bbox: {x,y,w,h}}]
    lines = [clean_line(l['text']) for l in lines_with_boxes]
    lines = [l for l in lines if l]
    joined = "\n".join(lines)

    store_name = infer_store(lines)
    address, city, cap = infer_address_city_cap(lines)
    vat = infer_vat(lines)
    dt_iso = infer_datetime(lines) or datetime.now(timezone.utc).isoformat()
    currency = infer_currency(joined)
    items = infer_items(lines)
    totals = infer_totals(lines, items)

    # Map line index -> bbox
    idx_to_bbox = {idx: lb['bbox'] for idx, lb in enumerate(lines_with_boxes) if (clean_line(lb['text']) or None)}
    # Try find store line index
    try:
        store_idx = lines.index(store_name) if store_name else 0
    except ValueError:
        store_idx = 0
    store_bbox = idx_to_bbox.get(store_idx)

    return {
        "store": {"name": store_name or "", "address": address, "city": city, "postalCode": cap, "vatNumber": vat},
        "storeOcrX": (store_bbox or {}).get('x'),
        "storeOcrY": (store_bbox or {}).get('y'),
        "storeOcrW": (store_bbox or {}).get('w'),
        "storeOcrH": (store_bbox or {}).get('h'),
        "datetime": dt_iso,
        "currency": currency or "EUR",
        "lines": [
            {
                "labelRaw": it["label"],
                "qty": float(it["qty"]),
                "unitPrice": float(it["unit"]),
                "lineTotal": float(it["total"]),
                "vatRate": float(it["vat"]) if it.get("vat") is not None else None,
                "weightKg": float(it["weightKg"]) if it.get("weightKg") is not None else None,
                "pricePerKg": float(it["pricePerKg"]) if it.get("pricePerKg") is not None else None,
                "ocrX": idx_to_bbox.get(it.get("_index"), {}).get('x'),
                "ocrY": idx_to_bbox.get(it.get("_index"), {}).get('y'),
                "ocrW": idx_to_bbox.get(it.get("_index"), {}).get('w'),
                "ocrH": idx_to_bbox.get(it.get("_index"), {}).get('h'),
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
    # Replace separators and odd glyphs often seen in receipts
    s = s.replace('|', ' ').replace('«', ' ').replace('»', ' ')
    # Normalize quotes/apostrophes
    s = s.replace('’', "'").replace('`', "'")
    # Collapse whitespace
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
    s = s.replace("S", "5").replace("s", "5")
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

# Override with a more robust EU/IT parser to avoid decimal-separator mistakes
def _parse_number_eu(s: str) -> Optional[float]:
    if s is None:
        return None
    s = s.strip()
    if not s:
        return None
    s = s.replace("S", "5").replace("s", "5")
    s = re.sub(r"[€$£]\s?", "", s)
    s = s.replace("\u00A0", " ")
    s = s.replace("'", "")
    s = re.sub(r"\s+", "", s)
    if "," in s and "." in s:
        last_comma = s.rfind(",")
        last_dot = s.rfind(".")
        if last_comma > last_dot:
            s = s.replace(".", "").replace(",", ".")
        else:
            s = s.replace(",", "")
    elif "," in s and "." not in s:
        s = s.replace(",", ".")
    m = re.findall(r"-?\d+(?:\.\d+)?", s)
    if not m:
        return None
    try:
        return float(m[-1])
    except Exception:
        return None

# Rebind global name so all uses go through the robust implementation
parse_number = _parse_number_eu


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



def _strip_vat_tokens(label: str, existing: Optional[float] = None) -> Tuple[str, Optional[float]]:
    # Capture percentages like "22%" and patterns like "IVA 22", "I.V.A. 10%", "Aliq 4".
    pct_pattern = re.compile(r"(\d{1,2}(?:[\.,]\d+)?%)")
    iva_pattern = re.compile(
        r"((?:\bIVA\b|\bI\.?V\.?A\.?\b|\bALIQ(?:UOTA)?\b)[^\d%]{0,10})(\d{1,2}(?:[\.,]\d+)?)(%?)",
        re.I,
    )
    vat_value = existing
    cleaned = label

    # First, handle explicit percentages
    for match in pct_pattern.findall(label):
        val = parse_number(match.replace('%', ''))
        if val is not None and 0 < val <= 24:
            vat_value = val
            cleaned = cleaned.replace(match, ' ')

    # Then, handle tokens preceded by IVA/ALIQ with optional percent sign
    for m in iva_pattern.finditer(cleaned):
        num = m.group(2)
        val = parse_number(num)
        if val is not None and 0 < val <= 24:
            vat_value = val
            cleaned = cleaned.replace(m.group(0), ' ')

    return re.sub(r"\s+", " ", cleaned).strip(), vat_value


def _parse_weight_line(line: str) -> Optional[dict[str, float]]:
    weight_re = re.compile(r"(?P<weight>\d+(?:[\.,]\d+)?)\s*(kg|kgs|kil?o|hg|ett|g|gr|grammi)\b", re.I)
    match = weight_re.search(line)
    if not match:
        return None
    weight = parse_number(match.group('weight'))
    if weight is None:
        return None
    unit_str = match.group(2).lower()
    if unit_str.startswith('g'):
        weight = weight / 1000.0
    elif unit_str.startswith('hg') or 'ett' in unit_str:
        weight = weight / 10.0
    price_re = re.compile(r"(?P<unit>\d+[\.,]\d+)\s*(?:€|eur)?\s*(?:/|al)?\s*(?:kg|hg|g)\b", re.I)
    price_match = price_re.search(line)
    if not price_match:
        price_match = re.search(r"[x@]\s*(?P<unit>\d+[\.,]\d+)", line, re.I)
    price_per_unit = parse_number(price_match.group('unit')) if price_match else None
    totals = [parse_number(m.group()) for m in re.finditer(r"\d+[\.,]\d{2}", line)]
    totals = [t for t in totals if t is not None]
    total_value = totals[-1] if totals else None
    return {
        'weight': weight,
        'price_per_unit': price_per_unit,
        'total': total_value,
    }


def _remove_weight_tokens(label: str) -> str:
    return re.sub(r"\b\d+(?:[\.,]\d+)?\s*(?:kg|kgs|kil?o|hg|ett|g|gr|grammi)\b", ' ', label, flags=re.I).strip()


def infer_items(lines: list[str]) -> list[dict]:
    items: list[dict] = []
    visited: set[int] = set()
    price_only_re = re.compile(r"^\s*(?:€|EUR)?\s*(-?\d+[\.,]\d{2})\s*$", re.I)
    qty_x_price_re = re.compile(r"(?P<label>.+?)\s+(?P<qty>\d+(?:[\.,]\d+)?)\s*[xX]\s*(?P<unit>-?\d+[\.,]\d{2})\s+(?P<tot>-?\d+[\.,]\d{2})(?:.*?(?P<vat>\d{1,2}(?:[\.,]\d+)?%))?$")
    label_price_re = re.compile(r"(?P<label>.+?)\s+(?P<tot>-?\d+[\.,]\d{2})(?:.*?(?P<vat>\d{1,2}(?:[\.,]\d+)?%))?$")
    blacklist_re = re.compile(r"\b(TOTALE\s+COMPLESSIVO|TOTALE\s+EURO|TOTALE|SUBTOTALE|PAGAMENTO|RESTO|SCONTO|BANCOMAT|CARTA\s+DI\s+CREDITO|CONTANTI|ARTICOLI|IMPORTO|CASSA|SEPARATORE|DI\s*CUI\s*IVA)\b", re.I)
    price_fallback = re.compile(r"(?P<val>-?\d+[\.,]\d{2})")
    count = len(lines)
    i = 0
    while i < count:
        if i in visited:
            i += 1
            continue
        raw = lines[i]
        if not raw:
            i += 1
            continue
        text_line = raw.strip()
        has_amount = bool(price_fallback.search(text_line))
        if not text_line:
            i += 1
            continue
        # Allow 'REPARTO' lines only if they contain a price amount
        if blacklist_re.search(text_line) and not has_amount:
            i += 1
            continue
        qty = None
        unit_price = None
        total = None
        vat = None
        label = None
        matched = False
        m = qty_x_price_re.match(text_line)
        if m:
            matched = True
            label = m.group('label').strip()
            qty = parse_number(m.group('qty')) or 1.0
            unit_price = parse_number(m.group('unit'))
            total = parse_number(m.group('tot'))
            vat = parse_number(m.group('vat').replace('%', '')) if m.group('vat') else None
        else:
            m2 = label_price_re.match(text_line)
            if m2:
                matched = True
                label = m2.group('label').strip()
                total = parse_number(m2.group('tot'))
                qty = 1.0
                unit_price = total
                vat = parse_number(m2.group('vat').replace('%', '')) if m2.group('vat') else None
        if not matched:
            prices = list(price_fallback.finditer(text_line))
            if prices:
                potential_total = parse_number(prices[-1].group('val'))
                label_candidate = text_line[:prices[-1].start()].strip()
                if potential_total is not None and label_candidate and not blacklist_re.search(label_candidate):
                    label = label_candidate
                    total = potential_total
                    qty = 1.0
                    unit_price = total
                    vat_match = re.search(r"(\d{1,2}(?:[\.,]\d+)?%)", text_line)
                    if vat_match:
                        vat = parse_number(vat_match.group(1).replace('%', ''))
        # If we have a plausible label but no total yet, try to merge weight + trailing price-only line
        pending_weight = None
        if label is not None and total is None:
            # Next line as weight and next+1 as price-only
            if i + 1 < count and (i + 1) not in visited:
                w = _parse_weight_line(lines[i + 1])
                if w:
                    # Prefer total on weight line; otherwise look at next line only if it's just a price
                    wt_total = w.get('total')
                    next_price = None
                    if wt_total is None and (i + 2) < count and (i + 2) not in visited:
                        mprice = price_only_re.match(lines[i + 2])
                        if mprice:
                            next_price = parse_number(mprice.group(1))
                    if wt_total is not None or next_price is not None:
                        pending_weight = w
                        visited.add(i + 1)
                        if next_price is not None:
                            total = next_price
                            visited.add(i + 2)
                        else:
                            total = wt_total
            # Or previous line as weight and next line as price-only
            if pending_weight is None and i > 0 and (i - 1) not in visited:
                w = _parse_weight_line(lines[i - 1])
                if w:
                    wt_total = w.get('total')
                    next_price = None
                    if wt_total is None and (i + 1) < count and (i + 1) not in visited:
                        mprice = price_only_re.match(lines[i + 1])
                        if mprice:
                            next_price = parse_number(mprice.group(1))
                    if wt_total is not None or next_price is not None:
                        pending_weight = w
                        visited.add(i - 1)
                        if next_price is not None:
                            total = next_price
                            visited.add(i + 1)
                        else:
                            total = wt_total
        if label is None or total is None:
            i += 1
            continue
        label, vat = _strip_vat_tokens(label, vat)
        label = _remove_weight_tokens(label)
        if not label:
            i += 1
            continue
        if vat is not None and not (0 < vat <= 24):
            vat = None
        if unit_price is None or unit_price == 0:
            unit_price = total / max(qty or 1.0, 1e-6)
        item = {
            '_index': i,
            'label': label,
            'qty': qty or 1.0,
            'unit': unit_price,
            'total': total,
            'vat': vat,
        }
        weight_info = pending_weight if 'pending_weight' in locals() and pending_weight else None
        if weight_info is None and i + 1 < count and (i + 1) not in visited:
            maybe_w = _parse_weight_line(lines[i + 1])
            if maybe_w:
                weight_info = maybe_w
                visited.add(i + 1)
        if not weight_info and i > 0 and (i - 1) not in visited:
            prev_weight = _parse_weight_line(lines[i - 1])
            if prev_weight:
                weight_info = prev_weight
                visited.add(i - 1)
        if weight_info and weight_info.get('weight'):
            weight = weight_info['weight']
            item['qty'] = weight
            item['weightKg'] = weight
            price_per_unit = weight_info.get('price_per_unit')
            if price_per_unit:
                item['unit'] = price_per_unit
                item['pricePerKg'] = price_per_unit
            else:
                item['unit'] = item['total'] / max(weight, 1e-6)
            if weight_info.get('total'):
                item['total'] = weight_info['total']
        items.append(item)
        i += 1
    items.sort(key=lambda it: it['_index'])
    for item in items:
        item.pop('_index', None)
    return items
def infer_totals(lines: list[str], items: list[dict]) -> dict:
    totals = {"subtotal": 0.0, "tax": 0.0, "total": 0.0}
    # Candidates
    total_cands: list[tuple[int, float]] = []  # (idx, value)
    subtotal_cands: list[tuple[int, float]] = []
    tax_cands: list[tuple[int, float]] = []

    # Helper regex
    re_total = re.compile(r"(?<!sub)totale(?:\s+(?:euro|da\s+pagare))?[:\s€]*([-]?\d+[\.,]\d{2})|importo\s*totale[:\s€]*([-]?\d+[\.,]\d{2})", re.I)
    re_sub = re.compile(r"sub\s*totale[:\s]*([-]?\d+[\.,]\d{2})", re.I)
    # Match IVA amount with optional currency symbol and separators
    re_tax_amount = re.compile(r"(?:di\s*cui\s*)?iva[^\d%€]*[:\-\s]*[€]?(\s*)?([-]?\d+[\.,]\d{2})", re.I)
    re_tax_pct = re.compile(r"iva[^\d%]*(\d{1,2}(?:[\.,]\d+)?)%", re.I)

    for idx, l in enumerate(lines):
        m = re_total.search(l)
        if m:
            grp = m.group(1) or m.group(2)
            v = parse_number(grp)
            if v is not None:
                total_cands.append((idx, v))
        m = re_sub.search(l)
        if m:
            v = parse_number(m.group(1))
            if v is not None:
                subtotal_cands.append((idx, v))
        # Only capture monetary amounts for IVA, ignore pure percentages
        m = re_tax_amount.search(l)
        if m and not re.search(r"%", m.group(0)):
            # prefer the numeric capture group with amount
            grp = m.group(2) if m.lastindex and m.lastindex >= 2 else m.group(1)
            v = parse_number(grp)
            if v is not None:
                tax_cands.append((idx, v))

    subtotal_est = sum(it["total"] for it in items)
    # Choose subtotal
    if subtotal_cands:
        # Take the one closest to items sum
        subtotal = min((abs(v - subtotal_est), v) for _, v in subtotal_cands)[1]
    else:
        subtotal = subtotal_est

    # Choose total
    if total_cands:
        # Prefer bottom-most candidate reasonably close to subtotal
        # Score by distance from subtotal and by position weight
        scored = []
        n = len(lines)
        for idx, v in total_cands:
            dist = abs(v - subtotal)
            posw = 1.0 + (idx / max(n, 1))  # prefer later lines
            scored.append((dist * 1.0 / posw, v))
        total = min(scored)[1]
    else:
        # Fallback: take last monetary amount in bottom lines
        bottom = lines[-6:]
        last_val = None
        for l in bottom[::-1]:
            m = re.search(r"(-?\d+[\.,]\d{2})", l)
            if m:
                last_val = parse_number(m.group(1))
                if last_val is not None:
                    break
        total = last_val or 0.0

    # Choose tax
    tax = 0.0
    if tax_cands:
        # Prefer bottom-most tax amount within the last 8 lines
        last_window_start = max(0, len(lines) - 8)
        window = [c for c in tax_cands if c[0] >= last_window_start]
        if window:
            tax = window[-1][1]
        else:
            tax = tax_cands[-1][1]
    elif total and subtotal and total >= subtotal:
        tax = total - subtotal

    # Sanity checks
    if subtotal < 0:
        subtotal = 0.0
    if total == 0.0:
        total = max(subtotal + tax, subtotal)
    # If tax looks implausible (>50% of total), try recompute from any IVA %
    if total > 0 and tax > total * 0.5:
        # Scan for IVA % and compute tax from subtotal (take highest reasonable <= 25%)
        pct_vals: list[float] = []
        for l in lines:
            m = re_tax_pct.search(l)
            if m:
                pv = parse_number(m.group(1))
                if pv is not None and 0 < pv <= 25:
                    pct_vals.append(pv)
        if pct_vals:
            tax = round(subtotal * (max(pct_vals) / 100.0), 2)
    # If total still far from subtotal+tax, snap to that
    if abs((subtotal + tax) - total) > max(0.2, (subtotal + tax) * 0.2):
        total = round(subtotal + tax, 2)

    return {"subtotal": float(subtotal), "tax": float(tax), "total": float(total)}
