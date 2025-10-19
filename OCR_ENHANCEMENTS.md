### Enhanced Name Matching & Commercial Activities Support

**Sistema di Matching Avanzato per Tutte le Attivita Commerciali**

Il sistema WIB include un **Enhanced Name Matcher** che gestisce TUTTI i tipi di attività commerciali con algoritmi di fuzzy matching avanzati e supporto completo per unità di misura.

#### Attività Supportate

- **Supermercati e alimentari**: Coop, Conad, Esselunga, Carrefour, LIDL, Eurospin, PAM, Auchan, Bennet, Despar
- **Discount**: MD, Ins, Penny Market, Tuodì, Todis, Ard, Aldi  
- **Ristoranti e fast food**: McDonalds, Burger King, KFC, Pizza Hut, Dominos, Subway, Autogrill
- **Bar e caffetterie**: Starbucks, Costa Coffee, bar locali, pasticcerie, gelaterie
- **Farmacie**: farmacie comunali, Lloyds, Boots, catene locali
- **Stazioni di servizio**: ENI/Agip, Shell, Q8, Esso, IP, Tamoil, Total, API, Repsol
- **Elettronica**: MediaWorld, Unieuro, Euronics, Expert, Trony
- **Moda**: Zara, H&M, Uniqlo, Bershka, OVS, Coin
- **Casa e giardino**: IKEA, Leroy Merlin, Bricocenter, OBI, Castorama
- **Tabaccherie e servizi**: tabacchi, ricevitorie, Sisal, Lottomatica
- **Specializzati**: macellerie, panetterie, alimentari, salumerie, ortofrutta

#### Algoritmi di Matching Avanzati

- **Levenshtein Similarity**: correzione errori OCR e typos  
- **Jaro-Winkler Similarity**: ottimizzato per matching nomi commerciali
- **Jaccard Similarity**: confronto basato su n-grammi
- **Combined Score**: media pesata (40% + 40% + 20%) per massima accuratezza

#### Matching Multi-Parametro con Location Data

- **Nome/Brand**: normalizzazione con dizionario di 100+ varianti commerciali
- **Location Data**: confronto indirizzo, città, CAP (30% del peso totale)
- **P.IVA**: match esatto con massima priorità (40% del peso location) 
- **Chain Recognition**: bonus per riconoscimento catene commerciali
- **Cache Performance**: Memory cache 30min per store e product data

#### Gestione Completa Unità di Misura

**Tipi supportati:**
- **Peso**: kg, g, grammi, chilogrammi (normalizzazione automatica)
- **Volume**: l, ml, cl, litri (conversione in litri standard)  
- **Lunghezza**: m, cm, mm, metri
- **Area**: m², mq, metri quadri
- **Quantità**: pz, pezzi, confezioni, pack

**Prezzo standardizzato:**
- Riconoscimento "€/kg", "al chilo", "prezzo al chilo"
- Calcolo automatico prezzo per unità (€/kg, €/l)
- Pattern recognition: "1kg pasta", "latte 1L", "2x pizze", "500g formaggio"

**Esempio utilizzo:**
```csharp
// Standard matching (solo nome)
var match = await nameMatcher.MatchStoreAsync("coop centro", ct);

// Enhanced matching (con location data)  
var enhancedMatch = await nameMatcher.MatchStoreAsync(
    "cooperativa", "via roma 123", "milano", "IT12345678901", ct);

// Unit measurement extraction
var units = UnitMeasurementHelper.ExtractUnits("Latte fresco 1L");
var pricePerLiter = UnitMeasurementHelper.CalculatePricePerUnit(
    "Latte 1L", unitPrice: 1.50m);
```

#### Miglioramenti Performance

- **Threshold dinamici**: 0.65 con location data, 0.78 solo nome
- **Caching intelligente**: 30min TTL con logging dettagliato  
- **OCR error handling**: 15+ pattern di correzione comuni
- **Brand normalization**: 100+ varianti di nomi commerciali

Questo sistema garantisce riconoscimento accurato di qualsiasi tipo di attività commerciale italiana, da supermercati a bar, da benzinai a tabaccherie, con supporto completo per prodotti venduti al peso, volume o quantità.




## Nota OCR (Tesseract, ottobre 2025)

- Il servizio `services/ocr` include ora un pre‑processing robusto (deskew, denoise, CLAHE, adaptive threshold) estratto in `services/ocr/preprocessing.py`.
- Tesseract è configurabile via variabili d’ambiente:
  - `TESSERACT_LANG` (default `ita+eng`)
  - `TESSERACT_PSM` (default `6`)
  - `TESSERACT_OEM` (default `3`)
- Dettagli e comandi in README, sezione "Configurazione OCR/KIE". Esempio rapido:
  - `docker compose build ocr && docker compose up -d ocr`
  - `curl -F "file=@docs/sample_receipt.jpg" http://localhost:8081/extract`
