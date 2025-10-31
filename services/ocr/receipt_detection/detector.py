import os
from typing import Tuple, Optional, Dict

import cv2
import numpy as np

_MODEL = None
_CFG = {
    "conf": float(os.getenv("RECEIPT_DETECTOR_CONF", "0.25")),
    "iou": float(os.getenv("RECEIPT_DETECTOR_IOU", "0.5")),
    "device": os.getenv("RECEIPT_DETECTOR_DEVICE", "auto"),
    "weights": os.getenv("RECEIPT_DETECTOR_WEIGHTS", "/models/receipt_yolov8n.pt"),
}


def _try_load_yolo():
    global _MODEL
    if _MODEL is not None:
        return _MODEL
    try:
        from ultralytics import YOLO  # type: ignore
        weights = _CFG["weights"]
        # If weights file missing, try to download default yolov8n.pt into /models
        if not os.path.exists(weights):
            os.makedirs(os.path.dirname(weights), exist_ok=True)
            try:
                # Download model using ultralytics API; this stores to cache, then we copy
                m = YOLO("yolov8n.pt")
                # Save/export to the configured path if possible
                try:
                    m.save(weights)
                except Exception:
                    pass
                _MODEL = m
            except Exception:
                _MODEL = None
                return None
        else:
            _MODEL = YOLO(weights)
        try:
            _MODEL.fuse()
        except Exception:
            pass
    except Exception:
        _MODEL = None
    return _MODEL


def _largest_contour_quad(gray: np.ndarray) -> Optional[np.ndarray]:
    # First, try threshold-based segmentation (robust for high-contrast rectangles)
    try:
        thr = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)[1]
        cnts, _ = cv2.findContours(thr, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if cnts:
            cnts = sorted(cnts, key=cv2.contourArea, reverse=True)
            h, w = gray.shape[:2]
            for c in cnts[:5]:
                rect = cv2.minAreaRect(c)
                box = cv2.boxPoints(rect)
                area = cv2.contourArea(box.astype(np.float32))
                full_area = (w * h)
                if area >= full_area * 0.98:
                    # Likely full image, ignore
                    continue
                if area >= full_area * 0.05:
                    return box.astype(np.float32)
    except Exception:
        pass
    # CLAHE to boost contrast
    clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
    g = clahe.apply(gray)
    edges = cv2.Canny(g, 50, 150)
    # Close small gaps
    kernel = np.ones((3, 3), np.uint8)
    edges = cv2.morphologyEx(edges, cv2.MORPH_CLOSE, kernel, iterations=1)
    contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None
    # Pick largest by area
    contours = sorted(contours, key=cv2.contourArea, reverse=True)
    h, w = gray.shape[:2]
    for c in contours[:5]:
        peri = cv2.arcLength(c, True)
        approx = cv2.approxPolyDP(c, 0.02 * peri, True)
        # Accept quadrilateral of plausible size/aspect
        if len(approx) == 4:
            area = cv2.contourArea(approx)
            if area < (w * h) * 0.05:  # too small
                continue
            return approx.reshape(4, 2).astype(np.float32)
        else:
            # Use minAreaRect as fallback to build a quad
            rect = cv2.minAreaRect(c)
            box = cv2.boxPoints(rect)
            area = cv2.contourArea(box.astype(np.float32))
            if area < (w * h) * 0.05:
                continue
            return box.astype(np.float32)
    return None


def _order_points(pts: np.ndarray) -> np.ndarray:
    # Order pts as [tl, tr, br, bl]
    rect = np.zeros((4, 2), dtype="float32")
    s = pts.sum(axis=1)
    rect[0] = pts[np.argmin(s)]
    rect[2] = pts[np.argmax(s)]
    diff = np.diff(pts, axis=1)
    rect[1] = pts[np.argmin(diff)]
    rect[3] = pts[np.argmax(diff)]
    return rect


def _four_point_transform(image: np.ndarray, pts: np.ndarray) -> np.ndarray:
    rect = _order_points(pts)
    (tl, tr, br, bl) = rect
    # compute width
    widthA = np.linalg.norm(br - bl)
    widthB = np.linalg.norm(tr - tl)
    maxWidth = int(max(widthA, widthB))
    # compute height
    heightA = np.linalg.norm(tr - br)
    heightB = np.linalg.norm(tl - bl)
    maxHeight = int(max(heightA, heightB))
    if maxWidth <= 0 or maxHeight <= 0:
        return image
    dst = np.array(
        [[0, 0], [maxWidth - 1, 0], [maxWidth - 1, maxHeight - 1], [0, maxHeight - 1]], dtype="float32"
    )
    M = cv2.getPerspectiveTransform(rect, dst)
    warped = cv2.warpPerspective(image, M, (maxWidth, maxHeight))
    return warped


def _deskew(image_gray: np.ndarray) -> Tuple[np.ndarray, float]:
    # Estimate skew via minAreaRect on edges
    edges = cv2.Canny(image_gray, 50, 150)
    coords = np.column_stack(np.where(edges > 0))
    if coords.size == 0:
        return image_gray, 0.0
    rect = cv2.minAreaRect(coords)
    angle = rect[-1]
    if angle < -45:
        angle = 90 + angle
    # rotate
    (h, w) = image_gray.shape[:2]
    M = cv2.getRotationMatrix2D((w // 2, h // 2), angle, 1.0)
    rotated = cv2.warpAffine(image_gray, M, (w, h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
    return rotated, float(angle)



def detect_and_warp(img_bgr: np.ndarray) -> Tuple[np.ndarray, Dict]:
    """
    Detect and rectify receipt region. Prefer full-image contour-based detection for robustness; fallback to YOLO bbox.
    Returns (warped_bgr, meta)
    """
    h, w = img_bgr.shape[:2]
    meta: Dict = {"bbox": None, "score": 0.0, "angle": 0.0, "used_gpu": False, "model_name": "none"}

    # 1) Try full-image quadrilateral detection
    gray_full = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
    quad_full = _largest_contour_quad(gray_full)
    if quad_full is not None:
        warped = _four_point_transform(img_bgr, quad_full)
        # bbox from quad
        xs = quad_full[:, 0]; ys = quad_full[:, 1]
        x1, y1 = int(max(0, np.min(xs))), int(max(0, np.min(ys)))
        x2, y2 = int(min(w - 1, np.max(xs))), int(min(h - 1, np.max(ys)))
        # Deskew
        gray2 = cv2.cvtColor(warped, cv2.COLOR_BGR2GRAY)
        _, ang = _deskew(gray2)
        if abs(ang) > 0.1:
            (hh, ww) = warped.shape[:2]
            M = cv2.getRotationMatrix2D((ww // 2, hh // 2), ang, 1.0)
            warped = cv2.warpAffine(warped, M, (ww, hh), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
        meta.update({"bbox": [x1, y1, x2, y2], "angle": float(ang)})
        return warped, meta

    # 2) Fallback: YOLO bbox (optional) then no warp
    model = _try_load_yolo()
    if model is not None:
        try:
            results = model.predict(source=img_bgr[..., ::-1], conf=_CFG["conf"], iou=_CFG["iou"], device=_CFG["device"], verbose=False)
            if results and hasattr(results[0], 'boxes') and results[0].boxes is not None and len(results[0].boxes) > 0:
                boxes = results[0].boxes
                confs = boxes.conf.cpu().numpy() if hasattr(boxes.conf, 'cpu') else boxes.conf.numpy()
                xyxy = boxes.xyxy.cpu().numpy() if hasattr(boxes.xyxy, 'cpu') else boxes.xyxy.numpy()
                idx = int(np.argmax(confs))
                score = float(confs[idx])
                x1, y1, x2, y2 = xyxy[idx]
                x1, y1, x2, y2 = int(max(0, x1)), int(max(0, y1)), int(min(w - 1, x2)), int(min(h - 1, y2))
                crop = img_bgr[y1:y2, x1:x2].copy()
                meta.update({"bbox": [x1, y1, x2, y2], "score": score, "model_name": getattr(model, 'name', 'yolov8')})
                return crop, meta
        except Exception:
            pass

    # 3) Last resort: return original image
    return img_bgr, meta
