from .detector import detect_and_warp

class ReceiptDetector:
    """Backwards-compatible stub wrapper over module-level detect_and_warp."""
    def __init__(self):
        pass
    def __call__(self, img_bgr):
        return detect_and_warp(img_bgr)
