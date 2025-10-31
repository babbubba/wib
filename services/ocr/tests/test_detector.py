import numpy as np
import cv2

from services.ocr.receipt_detection import detect_and_warp


def _synthetic_receipt(w=800, h=600, angle=15):
    img = np.zeros((h, w, 3), dtype=np.uint8)
    rect = np.array([[100, 50], [700, 50], [680, 550], [120, 550]], dtype=np.int32)
    M = cv2.getRotationMatrix2D((w//2, h//2), angle, 1.0)
    rect_rot = cv2.transform(rect[None, :, :], M)[0].astype(np.int32)
    cv2.fillConvexPoly(img, rect_rot, (200, 200, 200))
    return img


def test_detect_and_warp_reduces_area_for_receipt_like_image():
    img = _synthetic_receipt()
    warped, meta = detect_and_warp(img)
    assert isinstance(warped, np.ndarray)
    assert warped.size > 0
    # area reduced by >= 30%
    orig_area = img.shape[0] * img.shape[1]
    warped_area = warped.shape[0] * warped.shape[1]
    assert warped_area <= orig_area * 0.7


def test_detect_and_warp_no_receipt_fallback():
    # solid image (no edges), detection should fallback
    img = np.full((400, 400, 3), 255, dtype=np.uint8)
    warped, meta = detect_and_warp(img)
    assert meta.get('bbox') is None
    # warped equals original in size
    assert warped.shape[:2] == img.shape[:2]

