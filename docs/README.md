# Addestramento pratico: PP-Structure e Donut

Questa mini‑guida raccoglie script e comandi d’esempio per preparare un dataset da Label Studio/Doccano e avviare l’addestramento di modelli KIE con PP‑Structure (PaddleOCR) o Donut (OCR‑free). Gli esempi sono pensati per uso locale/offline come descritto in README.md del monorepo.

- Script conversione: `docs/convert_dataset.py`
- Mappatura etichette di esempio: `docs/label_map.example.json`
- Output dataset: sotto una cartella a scelta (es. `./.data/datasets`)


## Prerequisiti

- Python 3.10+
- (Opz) Pillow per leggere dimensioni immagine se l’export non le contiene: `pip install pillow`
- Dataset annotato con Label Studio (export JSON) o formato compatibile
- Spazio su disco per copie/riorganizzazione immagini

Nota: gli esempi di training qui sotto si basano sulle repo ufficiali (PaddleOCR per PP‑Structure e clovaai/donut). Verifica i nomi esatti dei file di config al momento dell’uso; potrebbero cambiare tra release.


## Conversione dataset (Label Studio → PP‑Structure/Donut)

Esempi base. I percorsi sono indicativi: adatta `--images-root` e `--input` ai tuoi file esportati.

- PP‑Structure (SER/RE, formato JSON lines con `ocr_info`+`relations`):
  - `python docs/convert_dataset.py --input ./labelstudio_export.json --images-root ./images --out-dir ./.data/datasets --target ppstructure --split 0.9 0.1 0 --label-map docs/label_map.example.json`
  - Output: `./.data/datasets/ppstructure/{train,val}/data.jsonl` + immagini organizzate in `images/`

- Donut (OCR‑free, JSONL con ground truth schema ricevuta):
  - `python docs/convert_dataset.py --input ./labelstudio_export.json --images-root ./images --out-dir ./.data/datasets --target donut --split 0.9 0.1 0 --label-map docs/label_map.example.json`
  - Output: `./.data/datasets/donut/{train,val}/data.jsonl` + immagini organizzate in `images/`

Opzioni utili:
- `--label-map`: mappa alias delle etichette (es. `store`, `date`, `total`, `line`, ecc.). Il file di esempio copre IT/EN.
- `--seed`: riproducibilità dello split.

Suggerimento: se il tuo export non contiene `original_width/height`, installa Pillow per leggere le dimensioni reali delle immagini.


## Strumenti di annotazione (opzionali ma consigliati)

Per creare/correggere dataset etichettati servono strumenti di annotazione. Abbiamo incluso nel compose due opzioni:

- Label Studio (consigliato per KIE su immagini, box e relazioni): http://localhost:8088
  - Avvio: `docker compose up -d labelstudio`
  - Volumi: `./.data/labelstudio` (progetti), `./.data/images` (immagini solo lettura).
  - Flow tipico: crea progetto → importa immagini da `/data` → definisci interfaccia (RectangleLabels, Relations, TextArea) → annota → Export JSON → usa `convert_dataset.py`.

- Doccano (consigliato per compiti testuali, es. classificazione linee): http://localhost:8089 (admin:admin)
  - Avvio: `docker compose up -d doccano`
  - Nota: non è pensato per annotare riquadri su immagini; può supportare dataset per modelli testuali (non gestiti da questo script di conversione). Se ti serve, possiamo aggiungere uno script separato per convertire export Doccano in esempi per il servizio ML (`/train`).


## Doccano → ML `/train` (classificazione tipo/categoria)

Se annoti esempi testuali (linee di scontrino ripulite) in Doccano per addestrare i suggerimenti di Tipo/Categoria del servizio ML, usa lo script dedicato:

- `python docs/convert_doccano_to_ml.py --input ./.data/doccano_export.jsonl --out ./.data/ml/train.json`

Mapping di default (personalizzabile con CLI):
- `text` → `labelRaw`
- `meta.typeId` → `finalTypeId` (fall-back: `labels[0]`)
- `meta.categoryId` → `finalCategoryId` (fall-back: `labels[1]` se esiste)
- `meta.brand` → `brand` (opzionale)

Invio diretto al servizio ML (opzionale):
- `python docs/convert_doccano_to_ml.py --input ./.data/doccano_export.jsonl --post http://localhost:8082/train`

Quindi puoi anche allenare caricando il JSON generato:
- `curl -X POST http://localhost:8082/train -H "Content-Type: application/json" --data @./.data/ml/train.json`


## Esempi training PP‑Structure (PaddleOCR)

1) Clona e prepara PaddleOCR (PP‑Structure):
- `git clone https://github.com/PaddlePaddle/PaddleOCR.git`
- `cd PaddleOCR`
- (Segui i requisiti CPU/GPU ufficiali; su CPU: `pip install -r requirements.txt` + paddlepaddle appropriato)

2) Punta ai file generati dallo script. In genere si modifica il file di config `configs/kie/...yml` oppure si usa `-o` per override.

Esempio (SER e RE separati):
- `python tools/train.py -c configs/kie/ser.yml -o Global.save_model_dir=./output/ser Train.dataset.label_file_list=["/percorso/assoluto/.data/datasets/ppstructure/train/data.jsonl"] Eval.dataset.label_file_list=["/percorso/assoluto/.data/datasets/ppstructure/val/data.jsonl"]`
- `python tools/train.py -c configs/kie/re.yml  -o Global.save_model_dir=./output/re  Train.dataset.label_file_list=["/percorso/assoluto/.data/datasets/ppstructure/train/data.jsonl"] Eval.dataset.label_file_list=["/percorso/assoluto/.data/datasets/ppstructure/val/data.jsonl"]`

Esempio (pipeline congiunta se supportata, `ser_re`):
- `python tools/train.py -c configs/kie/ser_re.yml -o Global.save_model_dir=./output/ser_re Train.dataset.label_file_list=["/percorso/assoluto/.data/datasets/ppstructure/train/data.jsonl"] Eval.dataset.label_file_list=["/percorso/assoluto/.data/datasets/ppstructure/val/data.jsonl"]`

Export/Deploy: salva i pesi risultanti; per integrarli nello stack, aggiorna `services/ocr` per caricare il modello KIE e produrre JSON compatibile con lo schema usato dall’endpoint `/kie` (vedi `services/ocr/main.py`).


## Esempi training Donut (clovaai/donut)

1) Clona la repo e installa i requisiti:
- `git clone https://github.com/clovaai/donut.git`
- `cd donut`
- `pip install -r requirements.txt`

2) Avvia il fine‑tuning partendo da un checkpoint (es. `naver-clova-ix/donut-base`). La repo include script/istruzioni per definire config e dataset in JSONL; punta i campi agli output `donut/{train,val}/data.jsonl` generati sopra.

Esempio generico:
- `python train.py --pretrained_model_name_or_path naver-clova-ix/donut-base --train_jsonl /percorso/assoluto/.data/datasets/donut/train/data.jsonl --val_jsonl /percorso/assoluto/.data/datasets/donut/val/data.jsonl --output_dir ./output/donut`

Nota: i nomi/parametri esatti possono variare tra versioni; fai riferimento alla documentazione della repo Donut che stai usando. Lo schema di ground truth generato è compatibile con la risposta di `/kie` del nostro servizio OCR.


## Integrazione nello stack WIB

- KIE (PP‑Structure o Donut): dopo l’addestramento, carica i pesi in `services/ocr`. È già collegato il caricamento tramite variabili d’ambiente:
  - `KIE_MODEL_DIR` (es. `/app/kie_models`) con mount locale `./.data/kie:/app/kie_models:ro` nel servizio `ocr`.
  - Opzionali: `PP_STRUCTURE_SER_CFG`, `PP_STRUCTURE_RE_CFG`, `DONUT_CHECKPOINT` se usi config/ckpt specifici.
  - L’endpoint `/kie` resta retro‑compatibile (accetta `{ text }`); se fornisci anche `image_b64` (base64), e i pesi sono disponibili, usa il motore reale, altrimenti effettua fallback allo stub.
- Validazione: misura F1 su campi (store/data/totali) e accuratezza/MAE sulle righe (qty/unitPrice/lineTotal). Mantieni fallback robusti (regex) come descritto in `README.md`.


## Troubleshooting rapido

- BBox sballati: controlla che il tuo export Label Studio includa `original_width/height` o che Pillow sia installato.
- Campi vuoti nel JSON Donut: aggiorna `docs/label_map.example.json` per mappare correttamente i `from_name` del tuo progetto Label Studio.
- Errori di path: gli output usano path relativi alla sottocartella `images/`; alcuni trainer richiedono path assoluti → converti se necessario o usa l’override con `-o` nelle config.
