import io
from PIL import Image, ImageDraw

from services.ocr.preprocessing import preprocess_image


def _img_bytes_with_text(angle: int = 10) -> bytes:
    base = Image.new("RGB", (500, 250), "white")
    draw = ImageDraw.Draw(base)
    draw.text((40, 90), "Latte 1L 1,29", fill="black")
    rotated = base.rotate(angle, expand=True, fillcolor="white")
    buf = io.BytesIO()
    rotated.save(buf, format="PNG")
    return buf.getvalue()


def test_preprocess_image_returns_image_L_mode():
    data = _img_bytes_with_text(12)
    out = preprocess_image(data)
    assert isinstance(out, Image.Image)
    # Output should be 8-bit single channel after pipeline
    assert out.mode in ("L", "1")
    assert out.size[0] > 0 and out.size[1] > 0

