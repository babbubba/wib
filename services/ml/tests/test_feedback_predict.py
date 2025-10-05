from fastapi.testclient import TestClient
from importlib import reload


def test_feedback_then_predict_returns_candidates(tmp_path, monkeypatch):
    # Reinitialize module with isolated model dir
    monkeypatch.setenv("MODEL_DIR", str(tmp_path))
    import services.ml.main as mlmod
    reload(mlmod)

    client = TestClient(mlmod.app)
    type_id = "00000000-0000-0000-0000-000000000001"
    cat_id = "00000000-0000-0000-0000-0000000000c8"

    r = client.post(
        "/feedback",
        json={
            "labelRaw": "LATTE 1L",
            "brand": "X",
            "finalTypeId": type_id,
            "finalCategoryId": cat_id,
        },
    )
    assert r.status_code == 200

    r = client.post("/predict", json={"labelRaw": "latte 1l"})
    assert r.status_code == 200
    data = r.json()
    assert data["typeCandidates"]
    assert data["typeCandidates"][0]["id"] == type_id

