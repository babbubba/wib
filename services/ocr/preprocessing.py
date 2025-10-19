import io
from typing import Optional

from PIL import Image, ImageOps, ImageFilter, ImageEnhance


def _deskew_cv2(arr):
    try:
        import numpy as np  # type: ignore
        import cv2  # type: ignore

        # Estimate skew angle using Hough lines on edges
        edges = cv2.Canny(arr, 50, 150)
        lines = cv2.HoughLines(edges, 1, np.pi / 180, threshold=120)
        angle_deg = 0.0
        if lines is not None and len(lines) > 0:
            angles = []
            for rho_theta in lines[:100]:
                rho, theta = rho_theta[0]
                a = (theta * 180.0 / np.pi) - 90.0
                if -45 <= a <= 45:
                    angles.append(a)
            if angles:
                angle_deg = float(np.median(angles))
        if abs(angle_deg) > 0.3:
            (h, w) = arr.shape[:2]
            M = cv2.getRotationMatrix2D((w // 2, h // 2), angle_deg, 1.0)
            arr = cv2.warpAffine(
                arr, M, (w, h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE
            )
        return arr
    except Exception:
        return arr


def _preprocess_cv2(img: Image.Image) -> Image.Image:
    try:
        import numpy as np  # type: ignore
        import cv2  # type: ignore

        arr = np.array(img)

        # Denoise gently to preserve edges
        arr = cv2.fastNlMeansDenoising(arr, h=10)

        # Deskew
        arr = _deskew_cv2(arr)

        # Local contrast via CLAHE
        clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
        arr = clahe.apply(arr)

        # Adaptive threshold to obtain crisp text strokes
        thr = cv2.adaptiveThreshold(
            arr, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 31, 10
        )

        # Slight morphological closing to close small gaps
        kernel = np.ones((2, 2), np.uint8)
        thr = cv2.morphologyEx(thr, cv2.MORPH_CLOSE, kernel, iterations=1)

        return Image.fromarray(thr)
    except Exception:
        # Fallback to PIL-only pipeline
        out = img
        out = ImageOps.autocontrast(out)
        out = out.filter(ImageFilter.MedianFilter(size=3))
        out = out.filter(ImageFilter.SHARPEN)
        out = ImageEnhance.Contrast(out).enhance(1.2)
        out = ImageEnhance.Brightness(out).enhance(1.05)
        return out


def preprocess_image(image_bytes: bytes) -> Image.Image:
    """Apply robust preprocessing to improve OCR accuracy.
    Steps: EXIF transpose, grayscale, optional OpenCV pipeline (denoise, deskew,
    CLAHE, adaptive threshold, morphology), upscale if small.
    Returns a PIL Image (8-bit) optimized for Tesseract.
    """
    with Image.open(io.BytesIO(image_bytes)) as raw:
        # Normalize orientation from EXIF
        img = ImageOps.exif_transpose(raw)
        # Convert to grayscale early
        img = ImageOps.grayscale(img)

        # Try OpenCV-based enhancements when available (with PIL fallback)
        out = _preprocess_cv2(img)

        # If very small, upscale to help Tesseract recognize small glyphs
        try:
            min_side = min(out.size)
            if min_side < 800:
                scale = 800.0 / float(min_side)
                new_size = (int(out.width * scale), int(out.height * scale))
                out = out.resize(new_size, Image.Resampling.LANCZOS)
        except Exception:
            pass

        return out

