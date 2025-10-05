from fastapi.testclient import TestClient
from importlib import reload


def test_predict(tmp_path, monkeypatch):
    monkeypatch.setenv("MODEL_DIR", str(tmp_path))
    import services.ml.main as mlmod
    reload(mlmod)
    client = TestClient(mlmod.app)
    r = client.post("/predict", json={"labelRaw": "milk"})
    assert r.status_code == 200
    assert r.json() == {"typeCandidates": [], "categoryCandidates": []}
