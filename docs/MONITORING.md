# Sistema di Monitoring e Logging Centralizzato

## Panoramica

Il sistema WIB ora dispone di un sistema completo di logging centralizzato e monitoring real-time per tutti i microservizi (Worker, API, ML, OCR).

## Architettura

### Backend (.NET & Python)

#### Logging su Redis Streams
Tutti i microservizi pubblicano messaggi strutturati su Redis Stream (chiave: `app_logs`).

**Struttura Messaggio:**
```json
{
  "timestamp": "2025-10-17T19:22:27.524Z",
  "level": "INFO|DEBUG|WARNING|ERROR|VERBOSE",
  "source": "worker|api|ml|ocr",
  "title": "Breve descrizione",
  "message": "Messaggio dettagliato",
  "metadata": {
    "key": "value",
    "stackTrace": "...",
    "correlationId": "..."
  }
}
```

#### Componenti Backend

**Librerie Condivise:**
- `WIB.Application/Interfaces/IRedisLogger.cs` - Interfaccia logging .NET
- `WIB.Infrastructure/Logging/RedisLogger.cs` - Implementazione .NET
- `WIB.Infrastructure/Logging/RedisLogConsumer.cs` - Consumer per stream e query
- `services/shared/redis_logger.py` - Logger Python

**API Endpoints:**
- `GET /monitoring/logs/stream` - SSE stream real-time (con filtri opzionali)
- `GET /monitoring/logs?limit=100&level=ERROR&source=worker` - Query log recenti
- `GET /monitoring/logs/error-count` - Conteggio errori recenti (ultimi 5 minuti)
- `GET /monitoring/services/status` - Stato salute di tutti i servizi

Tutti gli endpoint richiedono autenticazione con ruolo `wmc`.

### Frontend (Angular 19)

#### Pagina Monitoring (`/monitoring`)

**Componenti:**
- `monitoring.service.ts` - Servizio per comunicazione con API
- `service-status/` - Componente per visualizzare stato servizi
- `log-viewer/` - Componente principale per visualizzare log in real-time
- `monitoring.component.ts` - Pagina container

**Funzionalità Log Viewer:**
- Streaming real-time via Server-Sent Events (SSE)
- Filtri per livello (ERROR, WARNING, INFO, DEBUG, VERBOSE)
- Filtri per sorgente (worker, api, ml, ocr)
- Ricerca testuale nel messaggio/titolo
- Pause/Resume dello streaming
- Auto-scroll configurabile
- Dettaglio espandibile per metadata/stack trace
- Color coding per gravità:
  - Rosso: ERROR
  - Giallo: WARNING
  - Blu: INFO
  - Grigio: DEBUG/VERBOSE
- Limite 500 messaggi in memoria (configurabile)

**Funzionalità Service Status:**
- Polling automatico ogni 15 secondi
- Card visiva per ogni servizio
- Indicatori stato: verde (running), rosso (stopped), giallo (unhealthy)
- Uptime e timestamp ultimo check
- Refresh manuale

#### Home Page
Link "Monitoring & Logs" con badge rosso che mostra il numero di errori recenti (ultimi 5 minuti).
Polling automatico ogni 30 secondi.

## Configurazione

### Variabili d'Ambiente

**Servizi .NET (API, Worker):**
```yaml
Logging__StreamKey: app_logs          # Chiave Redis stream
Logging__MinLevel: Info               # Livello minimo: Verbose|Debug|Info|Warning|Error
Redis__Connection: redis:6379         # Connection string Redis
```

**Servizi Python (OCR, ML):**
```yaml
REDIS_URL: redis://redis:6379         # URL Redis
LOG_STREAM_KEY: app_logs              # Chiave Redis stream
LOG_LEVEL: INFO                       # Livello minimo: VERBOSE|DEBUG|INFO|WARNING|ERROR
```

### Configurazione in `docker-compose.yml`

Le variabili sono già configurate in `docker-compose.yml` per tutti i servizi.

## Utilizzo

### Accesso alla Dashboard

1. Avvia lo stack completo:
   ```powershell
   docker compose up -d --build
   ```

2. Apri WMC: `http://localhost:4201`

3. Login con credenziali admin:
   - Username: `admin`
   - Password: `admin`

4. Dalla home page, clicca su "Monitoring & Logs"

### Visualizzazione Log

**Filtrare per Livello:**
- Clicca sui bottoni dei livelli (ERROR, WARNING, INFO, DEBUG, VERBOSE)
- I bottoni evidenziati mostrano i livelli attualmente visibili

**Filtrare per Sorgente:**
- Clicca sui bottoni delle sorgenti (worker, api, ml, ocr)
- I bottoni evidenziati mostrano le sorgenti attualmente visibili

**Ricerca Testuale:**
- Digita nel campo di ricerca per filtrare per messaggio/titolo
- La ricerca è case-insensitive e in tempo reale

**Pausa/Resume:**
- Clicca "Pausa" per fermare l'arrivo di nuovi log
- Utile per analizzare uno specifico momento
- Clicca "Riprendi" per riprendere lo streaming

**Auto-scroll:**
- ON (default): la lista scorre automaticamente al nuovo log
- OFF: la lista rimane ferma, devi scorrere manualmente

**Dettagli Metadata:**
- Clicca su una riga di log per espandere i dettagli
- Visualizza metadata aggiuntivi, stack trace, correlation ID, ecc.

### Monitoraggio Servizi

La sezione superiore mostra 4 card (Worker, API, ML, OCR) con:
- Stato attuale (In Esecuzione / Fermato / Non Salutare)
- Uptime del servizio
- Timestamp ultimo check
- Colore bordo sinistro indica lo stato

Refresh automatico ogni 15 secondi, oppure clicca "Aggiorna" per refresh immediato.

### Badge Errori nella Home

La home page mostra un badge rosso sul link "Monitoring & Logs" se ci sono errori recenti (ultimi 5 minuti).
Il conteggio si aggiorna automaticamente ogni 30 secondi.

## Visualizzare Log Direttamente in Redis

Per debug o troubleshooting, puoi accedere direttamente allo stream Redis:

```powershell
# Visualizza ultimi 10 log
docker exec -it wib-main-redis-1 redis-cli XRANGE app_logs - + COUNT 10

# Visualizza tutti i log
docker exec -it wib-main-redis-1 redis-cli XRANGE app_logs - +

# Lunghezza stream
docker exec -it wib-main-redis-1 redis-cli XLEN app_logs
```

## Logging nel Codice

### .NET (C#)

```csharp
public class MyService
{
    private readonly IRedisLogger _logger;

    public MyService(IRedisLogger logger)
    {
        _logger = logger;
    }

    public async Task DoSomethingAsync()
    {
        // Info semplice
        await _logger.InfoAsync("Operation started", "Operation title");

        try
        {
            // ... operazione ...

            // Debug con metadata
            await _logger.DebugAsync(
                "Processing item",
                "Item processed",
                new Dictionary<string, object>
                {
                    ["itemId"] = "12345",
                    ["duration"] = 123
                }
            );
        }
        catch (Exception ex)
        {
            // Error con eccezione
            await _logger.ErrorAsync(
                ex.Message,
                "Operation failed",
                new Dictionary<string, object>
                {
                    ["stackTrace"] = ex.StackTrace,
                    ["exceptionType"] = ex.GetType().Name
                }
            );
        }
    }
}
```

### Python

```python
from shared.redis_logger import RedisLogger

logger = RedisLogger(redis_url="redis://localhost:6379", source="myservice")

async def do_something():
    # Info semplice
    await logger.info("Operation started", "Operation title")

    try:
        # ... operazione ...

        # Debug con metadata
        await logger.debug(
            "Processing item",
            "Item processed",
            metadata={
                "item_id": "12345",
                "duration": 123
            }
        )
    except Exception as e:
        # Error con eccezione
        await logger.error(
            str(e),
            "Operation failed",
            metadata={
                "stack_trace": traceback.format_exc(),
                "exception_type": type(e).__name__
            }
        )
```

## Logging Strategico nei Microservizi

### Worker
- Dequeue operazione dalla coda
- Download immagine da MinIO
- Inizio elaborazione
- Chiamate a OCR/KIE/ML
- Salvataggio risultati
- Completamento con successo/errore

### API
- Ricezione richieste
- Validazione input
- Salvataggio su storage
- Accodamento su Redis
- Risposta al client
- Errori di validazione/autenticazione

### OCR
- Ricezione immagine
- Preprocessing
- Esecuzione OCR
- Estrazione KIE (stub vs modello)
- Numero di linee estratte

### ML
- Ricezione richiesta predizione
- Caricamento modello
- Inferenza
- Ricezione feedback
- Training modello

## Performance e Ritenzione

- **Stream trimming**: Redis mantiene automaticamente solo gli ultimi ~10,000 messaggi
- **Non-blocking**: Tutti i log sono asincroni e non bloccano le operazioni
- **Fallback**: Se Redis non è disponibile, i log vengono stampati su console
- **Rate limiting**: Implementato per evitare flooding (consigliato: max 100 msg/sec per servizio)

## Troubleshooting

### Log non appaiono nella dashboard

1. Verifica che Redis sia in esecuzione:
   ```powershell
   docker compose ps redis
   ```

2. Verifica che i servizi stiano loggando:
   ```powershell
   docker compose logs worker
   docker compose logs api
   ```

3. Verifica lo stream Redis:
   ```powershell
   docker exec -it wib-main-redis-1 redis-cli XLEN app_logs
   ```

4. Verifica che l'endpoint SSE sia accessibile (dopo login):
   ```powershell
   curl -H "Authorization: Bearer YOUR_JWT_TOKEN" http://localhost:8080/monitoring/logs/stream
   ```

### Servizi mostrano stato "unhealthy"

1. Verifica i container Docker:
   ```powershell
   docker compose ps
   ```

2. Verifica gli health check:
   ```powershell
   # OCR
   curl http://localhost:8081/health

   # ML
   curl http://localhost:8082/health

   # API
   curl http://localhost:8080/health/live
   ```

3. Verifica i log del servizio specifico:
   ```powershell
   docker compose logs <service-name>
   ```

### Badge errori non si aggiorna

1. Verifica che l'endpoint error-count funzioni:
   ```powershell
   curl -H "Authorization: Bearer YOUR_JWT_TOKEN" http://localhost:8080/monitoring/logs/error-count
   ```

2. Controlla la console del browser per errori JavaScript

3. Verifica che il token JWT sia valido e non scaduto

## Estensioni Future

Possibili miglioramenti:
- Persistenza log su database per storico lungo termine
- Dashboard con grafici di analytics (errori nel tempo, volume log per servizio)
- Alert configurabili (email/Slack su soglia errori)
- Aggregazione log per correlation ID (tracciare una richiesta end-to-end)
- Export log in CSV/JSON
- Integrazione con Grafana/Prometheus per metriche
