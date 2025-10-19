# Piano di Implementazione — Issue #8: Tesseract OCR Production Configuration

Autore: team WIB • Data: 2025-10-19 • Issue: https://github.com/babbubba/wib/issues/8

## Contesto
Il servizio `services/ocr` espone gli endpoint `/health`, `/extract` (OCR testo) e `/kie` (estrazione campi/righe). L’OCR corrente usa Tesseract con pre‑processing già presente in `main.py`. L’issue richiede di:
- Assicurare in container i pacchetti lingua e dipendenze per l’italiano.
- Estrarre il pre‑processing in un modulo dedicato con step espliciti: deskew, denoise, adaptive thresholding, contrast enhancement.
- Parametrizzare Tesseract via variabili d’ambiente: `TESSERACT_LANG`, `TESSERACT_PSM`, `TESSERACT_OEM` (default: `ita+eng`, `6`, `3`).
- Aggiornare test e documentazione.

## Obiettivi
- Migliorare robustezza e manutenibilità del pipeline OCR su scontrini italiani.
- Rendere configurabile l’esecuzione Tesseract in ambienti diversi senza rebuild.
- Coprire il pre‑processing con test unitari a basso attrito (senza dipendere da Tesseract nelle CI locali).

## Non‑obiettivi
- Non si abilita in questa fase un motore KIE addestrato (PP‑Structure/Donut restano opzionali come da README).
- Non si modifica l’API pubblica degli endpoint né le porte.

## Modifiche Pianificate
1) Dockerfile OCR
   - Verifica/integrazione pacchetti runtime: `tesseract-ocr`, `tesseract-ocr-ita` (già presenti), mantenendo `libgl1`, `libglib2.0-0` per OpenCV.
   - Valutazione opzionale (non bloccante): `tesseract-ocr-osd` per orientation detection; al momento il deskew è già gestito da OpenCV.

2) Pre‑processing modulare
   - Nuovo file `services/ocr/preprocessing.py` con funzioni:
     - `preprocess_image(image_bytes: bytes) -> PIL.Image`: orchestratore.
     - Step interni: `deskew_cv2`, `denoise_cv2`, `apply_clahe_cv2`, `adaptive_threshold_cv2`, `morph_close_cv2` con fallback PIL‑only (Median, Autocontrast, Sharpen, piccoli boost di contrasto/luminosità).
   - Spostare l’attuale logica da `main.py` a questo modulo, lasciando le stesse preimpostazioni (CLAHE clipLimit ~3.0, tile 8x8; adaptive threshold blockSize ~31, C ~10; NLM denoise `h=10`; upscale minimo lato 800 px).

3) Parametrizzazione Tesseract
   - In `services/ocr/main.py` leggere env:
     - `TESSERACT_LANG` (default `ita+eng`)
     - `TESSERACT_PSM` (default `6`)
     - `TESSERACT_OEM` (default `3`)
   - Generare `tess_config = f"--oem {OEM} --psm {PSM}"` e usarlo in `pytesseract.image_to_data` e `pytesseract.image_to_string` passando anche `lang=LANG`.
   - Sanitizzazione: cast a int con fallback ai default se non validi.

4) Refactor `main.py`
   - Importare `preprocess_image` dal nuovo modulo e rimuovere la copia locale.
   - Centralizzare le chiamate Tesseract sulle nuove config `lang`/`config`.
   - Nessun cambio JSON di risposta.

5) Test
   - Nuovo `services/ocr/tests/test_preprocessing.py`:
     - Crea immagine sintetica (PIL) con testo ruotato/rumoroso; verifica che `preprocess_image` restituisca una `PIL.Image` non vuota e con canale 8‑bit.
     - Se `cv2` disponibile: calcola angolo medio con Hough su immagine prima/dopo e attende riduzione |angolo| (tolleranza, es. ≥30% di miglioramento) — se non disponibile, fallback: verifica solo percorso PIL.
   - Manteniamo i test esistenti per `/health` e `/extract`; se in CI non è impostata `OCR_STUB=true`, aggiungeremo fixture/env override nei test per forzare lo stub quando si testa l’endpoint senza Tesseract.

6) Documentazione
   - README sezione “Configurazione OCR/KIE”: aggiungere i parametri env con default e suggerimenti pratici.
   - Esempio uso:
     - `docker compose build ocr && docker compose up -d ocr`
     - `curl -F "file=@docs/sample_receipt.jpg" http://localhost:8081/extract`
   - Nota: aggiungere un `docs/sample_receipt.jpg` dimostrativo o indicare dove posizionarlo localmente (evitare asset pesanti nel repo).

## Passi Dettagliati
1. Analisi stato attuale (completata)
   - `services/ocr/Dockerfile` già installa `tesseract-ocr` e `tesseract-ocr-ita` più librerie runtime.
   - `services/ocr/main.py` contiene già un pre‑processing robusto; va estratto in modulo separato e reso testabile.

2. Sviluppo
   - [ ] Aggiungere `services/ocr/preprocessing.py` e spostare la logica esistente, con le stesse costanti/parametri.
   - [ ] Aggiornare `services/ocr/main.py` per: import `preprocess_image`, leggere env `TESSERACT_*`, passare `lang=config` a pytesseract.
   - [ ] Valutare se aggiungere `tesseract-ocr-osd` nel Dockerfile (facoltativo) — lasciando invariato il comportamento di default.

3. Test
   - [ ] Aggiungere `services/ocr/tests/test_preprocessing.py` con i casi sopra.
   - [ ] Se necessario, in `services/ocr/tests/test_extract.py` forzare `OCR_STUB=true` via monkeypatch/env per stabilità locale della CI, oppure generare un’immagine sintetica semplice e verificare output non vuoto.

4. Documentazione
   - [ ] Aggiornare README: sezione “Configurazione OCR/KIE” con tabella env (`TESSERACT_LANG`, `TESSERACT_PSM`, `TESSERACT_OEM`) e valori consigliati per scontrini IT.
   - [ ] Aggiungere nota su `docs/sample_receipt.jpg` (o un link esterno/sorgente del sample).

5. Verifica manuale
   - [ ] `docker compose build ocr && docker compose up -d ocr`
   - [ ] `curl -F "file=@docs/sample_receipt.jpg" http://localhost:8081/extract` → verificare accuratezza percepita (>80% per sample indicato).
   - [ ] `pytest services/ocr/tests -q` deve passare.

## Mappatura ai Criteri di Successo (Issue)
- Build e run container OCR: coperto in “Verifica manuale”.
- Accuratezza >80% sul sample: attesa grazie a deskew + CLAHE + threshold + settaggi Tesseract `ita+eng`, `--psm 6`, `--oem 3`.
- Test `test_preprocessing.py`: aggiunto come parte del piano.
- Documentazione README aggiornata.

## Rischi e Mitigazioni
- Variabilità sample: parametri threshold/CLAHE potrebbero non generalizzare. Mitigazione: mantenere fallback PIL e parametri conservativi; eventuale tuning per marche/fotocamere comuni.
- Prestazioni: pre‑processing OpenCV può aumentare latenza su CPU. Mitigazione: uso NLM leggero (`h=10`), blocchi CLAHE 8×8, una sola passata morph.
- Dipendenze di sistema: dataset lingua già presenti; opzionale OSD non bloccante. I wheel Python sono preinstallati via cache.

## Rollout
- Change a basso impatto, backward‑compatible (default invariati). Deploy con rebuild del solo servizio OCR.
- Nessun cambiamento richiesto a API/Worker/Frontend.

## Backout Plan
- In caso di regressioni, revert a commit precedente: ripristina `main.py` come sorgente della funzione di pre‑processing e rimuove il modulo.

## Stima Sforzo
- Dev: ~0.5–1 g (refactor + env + test).
- Docs e tuning: ~0.5 g.

## TODO Operativi (checklist)
- [ ] Modulo `preprocessing.py` creato e integrato
- [ ] Env `TESSERACT_LANG/PSM/OEM` letti e usati
- [ ] Test `test_preprocessing.py` verdi
- [ ] README aggiornato (OCR/KIE)
- [ ] Verifica manuale con `curl` OK

