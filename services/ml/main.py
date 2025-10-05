from fastapi import FastAPI
from pydantic import BaseModel
from typing import List, Optional
import os
import json
from pathlib import Path

import joblib
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import SGDClassifier
from sklearn.exceptions import NotFittedError
import numpy as np


class PredictRequest(BaseModel):
    labelRaw: str
    brand: Optional[str] = None


class Candidate(BaseModel):
    id: str
    name: str
    conf: float


class PredictResponse(BaseModel):
    typeCandidates: List[Candidate] = []
    categoryCandidates: List[Candidate] = []


class FeedbackRequest(BaseModel):
    labelRaw: str
    brand: Optional[str] = None
    finalTypeId: str
    finalCategoryId: Optional[str] = None


class TrainRequest(BaseModel):
    # Optional batch train placeholder
    examples: List[FeedbackRequest] = []


class ModelManager:
    def __init__(self, model_dir: str, top_k: int = 3):
        self.model_dir = Path(model_dir)
        self.model_dir.mkdir(parents=True, exist_ok=True)

        self.vectorizer: TfidfVectorizer = TfidfVectorizer(
            analyzer="char", ngram_range=(3, 5)
        )
        # Two independent classifiers for type and category
        self.type_clf: SGDClassifier = SGDClassifier(loss="log_loss")
        self.cat_clf: SGDClassifier = SGDClassifier(loss="log_loss")
        self.type_labels: List[str] = []
        self.cat_labels: List[str] = []
        self.top_k = top_k

        self._fitted_vectorizer = False
        self._fitted_type = False
        self._fitted_cat = False

        self._load()
        # simple memory of seen texts per class (fallback when single class)
        self.type_memory: dict[str, list[str]] = {}
        self.cat_memory: dict[str, list[str]] = {}

    def _paths(self):
        return (
            self.model_dir / "vectorizer.joblib",
            self.model_dir / "type_clf.joblib",
            self.model_dir / "cat_clf.joblib",
            self.model_dir / "type_labels.json",
            self.model_dir / "cat_labels.json",
        )

    def _load(self):
        v_path, t_path, c_path, tl_path, cl_path = self._paths()
        try:
            if v_path.exists():
                self.vectorizer = joblib.load(v_path)
                self._fitted_vectorizer = True
            if t_path.exists() and tl_path.exists():
                self.type_clf = joblib.load(t_path)
                self.type_labels = json.loads(tl_path.read_text())
                self._fitted_type = True
            if c_path.exists() and cl_path.exists():
                self.cat_clf = joblib.load(c_path)
                self.cat_labels = json.loads(cl_path.read_text())
                self._fitted_cat = True
        except Exception:
            # if any load error occurs, start fresh
            self._fitted_vectorizer = False
            self._fitted_type = False
            self._fitted_cat = False

    def _save(self):
        v_path, t_path, c_path, tl_path, cl_path = self._paths()
        if self._fitted_vectorizer:
            joblib.dump(self.vectorizer, v_path)
        if self._fitted_type:
            joblib.dump(self.type_clf, t_path)
            tl_path.write_text(json.dumps(self.type_labels))
        if self._fitted_cat:
            joblib.dump(self.cat_clf, c_path)
            cl_path.write_text(json.dumps(self.cat_labels))

    def _ensure_vectorizer(self, texts: List[str]):
        if not self._fitted_vectorizer:
            X = self.vectorizer.fit_transform(texts)
            self._fitted_vectorizer = True
            return X
        return self.vectorizer.transform(texts)

    def feedback(self, label_raw: str, brand: Optional[str], final_type_id: str, final_cat_id: Optional[str]):
        # Create or update class lists
        if final_type_id not in self.type_labels:
            self.type_labels.append(final_type_id)
        if final_cat_id:
            if final_cat_id not in self.cat_labels:
                self.cat_labels.append(final_cat_id)

        text = self._combine(label_raw, brand)
        X = self._ensure_vectorizer([text])

        # Train type classifier
        # store memory
        self.type_memory.setdefault(final_type_id, []).append(text)
        y_type = np.array([self.type_labels.index(final_type_id)])
        # train only if we have at least 2 classes
        if len(self.type_labels) >= 2:
            if not self._fitted_type:
                self.type_clf.partial_fit(X, y_type, classes=np.arange(len(self.type_labels)))
                self._fitted_type = True
            else:
                self.type_clf.partial_fit(X, y_type, classes=np.arange(len(self.type_labels)))

        # Train category if provided
        if final_cat_id:
            self.cat_memory.setdefault(final_cat_id, []).append(text)
            if len(self.cat_labels) >= 2:
                y_cat = np.array([self.cat_labels.index(final_cat_id)])
                if not self._fitted_cat:
                    self.cat_clf.partial_fit(X, y_cat, classes=np.arange(len(self.cat_labels)))
                    self._fitted_cat = True
                else:
                    self.cat_clf.partial_fit(X, y_cat, classes=np.arange(len(self.cat_labels)))

        self._save()

    def predict(self, label_raw: str, brand: Optional[str]):
        text = self._combine(label_raw, brand)
        if not self._fitted_vectorizer:
            return [], []
        X = self.vectorizer.transform([text])
        type_candidates: List[Candidate] = []
        cat_candidates: List[Candidate] = []

        if self._fitted_type and len(self.type_labels) > 0:
            try:
                proba = self.type_clf.predict_proba(X)[0]
                idxs = np.argsort(proba)[::-1][: self.top_k]
                for i in idxs:
                    type_candidates.append(Candidate(id=self.type_labels[i], name="", conf=float(proba[i])))
            except Exception:
                pass
        elif len(self.type_labels) == 1:
            # Fallback: only one known class
            type_candidates.append(Candidate(id=self.type_labels[0], name="", conf=1.0))

        if self._fitted_cat and len(self.cat_labels) > 0:
            try:
                proba = self.cat_clf.predict_proba(X)[0]
                idxs = np.argsort(proba)[::-1][: self.top_k]
                for i in idxs:
                    cat_candidates.append(Candidate(id=self.cat_labels[i], name="", conf=float(proba[i])))
            except Exception:
                pass
        elif len(self.cat_labels) == 1:
            cat_candidates.append(Candidate(id=self.cat_labels[0], name="", conf=1.0))

        return type_candidates, cat_candidates

    @staticmethod
    def _combine(label_raw: str, brand: Optional[str]):
        return f"{label_raw} {brand}".strip() if brand else label_raw


MODEL_DIR = os.getenv("MODEL_DIR", "/app/models")
TOP_K = int(os.getenv("TOP_K", "3"))
manager = ModelManager(MODEL_DIR, top_k=TOP_K)

app = FastAPI()


@app.post("/predict", response_model=PredictResponse)
def predict(req: PredictRequest):
    t, c = manager.predict(req.labelRaw, req.brand)
    return PredictResponse(typeCandidates=t, categoryCandidates=c)


@app.post("/feedback")
def feedback(req: FeedbackRequest):
    manager.feedback(req.labelRaw, req.brand, req.finalTypeId, req.finalCategoryId)
    return {"status": "ok"}


@app.post("/train")
def train(req: TrainRequest):
    # Simple batch training from examples
    for ex in req.examples:
        manager.feedback(ex.labelRaw, ex.brand, ex.finalTypeId, ex.finalCategoryId)
    return {"status": "ok"}


@app.get("/health")
def health():
    return {"status": "ok"}
