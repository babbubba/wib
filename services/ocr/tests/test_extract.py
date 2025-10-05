from fastapi.testclient import TestClient
from services.ocr.main import app


def test_extract_returns_text():
    client = TestClient(app)
    files = {"file": ("test.jpg", b"abc", "image/jpeg")}
    r = client.post("/extract", files=files)
    assert r.status_code == 200
    assert r.json()["text"] == "mock-ocr"

