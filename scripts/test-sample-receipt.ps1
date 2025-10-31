<#
.SYNOPSIS
  Esegue un test end‑to‑end di upload scontrino + pipeline OCR/KIE/ML e confronta l’estratto con un JSON di riferimento.

.PARAMETER ImagePath
  Percorso dell’immagine da caricare (default: docs/sample_receipt.jpg)

.PARAMETER GroundTruth
  Percorso del JSON di riferimento (default: docs/sample_receipt.json)

.PARAMETER BaseUrl
  Base URL dell’API (default: http://localhost:8080)

.PARAMETER DeviceUser / DevicePass
  Credenziali utente con ruolo device (default: user/user)

.PARAMETER AdminUser / AdminPass
  Credenziali utente con ruolo wmc (default: admin/admin)

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/test-sample-receipt.ps1

.EXAMPLE
  powershell -File scripts/test-sample-receipt.ps1 -ImagePath docs/mio.jpg -GroundTruth docs/mio.json
#>

[CmdletBinding()]
param(
  [string]$ImagePath = "docs/sample_receipt.jpg",
  [string]$GroundTruth = "docs/sample_receipt.json",
  [string]$BaseUrl = "http://localhost:8080",
  [string]$DeviceUser = "user",
  [string]$DevicePass = "user",
  [string]$AdminUser = "admin",
  [string]$AdminPass = "admin",
  [int]$WaitSeconds = 8
)

$ErrorActionPreference = 'Stop'
function Write-Info($msg){ Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Write-Warn($msg){ Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Write-Err ($msg){ Write-Host "[ERROR] $msg" -ForegroundColor Red }

try {
  if (-not (Test-Path $ImagePath)) { throw "Image not found: $ImagePath" }
  if (-not (Test-Path $GroundTruth)) { throw "Ground truth not found: $GroundTruth" }

  Write-Info "Login device…"
  $loginDev = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method Post -ContentType application/json -Body (@{ username=$DeviceUser; password=$DevicePass } | ConvertTo-Json)
  $devToken = $loginDev.accessToken
  if (-not $devToken) { throw "Device login failed" }

  Write-Info "Upload immagine ($ImagePath)…"
  $uploadJson = (& curl.exe -s -X POST -H "Authorization: Bearer $devToken" -F "file=@$ImagePath" "$BaseUrl/receipts")
  if (-not $uploadJson) { throw "Upload failed (empty response)" }
  $uploadObj = $uploadJson | ConvertFrom-Json
  $objectKey = $uploadObj.objectKey
  if (-not $objectKey) { throw "Upload failed (no objectKey): $uploadJson" }
  Write-Info "Upload OK. objectKey=$objectKey"

  Write-Info "Attendo $WaitSeconds s per processing…"
  Start-Sleep -Seconds $WaitSeconds

  Write-Info "Login admin (wmc)…"
  $loginAdm = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method Post -ContentType application/json -Body (@{ username=$AdminUser; password=$AdminPass } | ConvertTo-Json)
  $admToken = $loginAdm.accessToken
  if (-not $admToken) { throw "Admin login failed" }
  $headers = @{ Authorization = "Bearer $admToken" }

  Write-Info "Recupero ultimo receipt…"
  $list = Invoke-RestMethod -Uri "$BaseUrl/receipts?take=1" -Headers $headers
  if (-not $list -or -not $list.id) { throw "Nessun receipt trovato" }
  $rid = $list.id
  $rec = Invoke-RestMethod -Uri "$BaseUrl/receipts/$rid" -Headers $headers

  # Logs docker (api, worker, ml, ocr, proxy)
  Write-Info "Ultimi log docker (api, worker, ml, ocr, proxy)…"
  try {
    $logs = docker compose logs --tail=120 api worker ml ocr proxy 2>&1 | Out-String
  } catch { $logs = "" }
  $okPatterns = @("Receipt Saved", "Persisting receipt", "OCR Complete", "KIE Complete")
  $errPatterns = @("Processing Failed", "ERROR", "Exception", "violates")
  $logsOk = $okPatterns | ForEach-Object { if ($logs -match [regex]::Escape($_)) { $_ } }
  $logsErr = $errPatterns | ForEach-Object { if ($logs -match $_) { $_ } }

  # Confronto con ground truth
  $gt = Get-Content -Raw $GroundTruth | ConvertFrom-Json
  $summary = [PSCustomObject]@{
    ObjectKey           = $objectKey
    ReceiptId           = $rid
    StoreName_Extracted = $rec.store.name
    StoreName_GT        = $gt.store.name
    Lines_Extracted     = ($rec.lines | Measure-Object).Count
    Lines_GT            = ($gt.lines | Measure-Object).Count
    Total_Extracted     = [decimal]$rec.totals.total
    Total_GT            = [decimal]$gt.totals.total
    Total_Delta         = [Math]::Abs(([decimal]$rec.totals.total) - ([decimal]$gt.totals.total))
    Tax_Extracted       = [decimal]$rec.totals.tax
    Tax_GT              = [decimal]$gt.totals.tax
    Tax_Delta           = [Math]::Abs(([decimal]$rec.totals.tax) - ([decimal]$gt.totals.tax))
    Logs_OK_Markers     = ($logsOk -join ", ")
    Logs_ERR_Markers    = ($logsErr -join ", ")
  }

  Write-Host "`n===== RESULT SUMMARY =====" -ForegroundColor Green
  $summary | Format-List | Out-String | Write-Host

  # Confronto riga per riga (index-aligned)
  function NormalizeText([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return "" }
    $s = $s.ToLowerInvariant().Trim()
    # remove diacritics
    $nf = [Text.NormalizationForm]::FormD
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.Normalize($nf).ToCharArray()) {
      if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
        [void]$sb.Append($ch)
      }
    }
    $s = $sb.ToString().Normalize([Text.NormalizationForm]::FormC)
    # keep alnum+space and collapse spaces
    $s = -join ($s.ToCharArray() | ForEach-Object { if ([char]::IsLetterOrDigit($_) -or [char]::IsWhiteSpace($_)) { $_ } else { ' ' } })
    $s = ($s -replace '\s+', ' ').Trim()
    return $s
  }

  $extLines = @($rec.lines)
  $gtLines  = @($gt.lines)
  $minCount = [Math]::Min($extLines.Count, $gtLines.Count)
  $lineComparisons = New-Object System.Collections.Generic.List[object]
  for ($i=0; $i -lt $minCount; $i++) {
    $e = $extLines[$i]
    $g = $gtLines[$i]
    $labelEq = (NormalizeText $e.labelRaw) -eq (NormalizeText $g.labelRaw)
    $qtyDelta  = [Math]::Abs(([decimal]$e.qty) - ([decimal]$g.qty))
    $upDelta   = [Math]::Abs(([decimal]$e.unitPrice) - ([decimal]$g.unitPrice))
    $totDelta  = [Math]::Abs(([decimal]$e.lineTotal) - ([decimal]$g.lineTotal))
    $vatE = $null; $vatG = $null
    try { $vatE = [decimal]$e.vatRate } catch {}
    try { $vatG = [decimal]$g.vatRate } catch {}
    $vatDelta = $null
    if ($vatE -ne $null -and $vatG -ne $null) { $vatDelta = [Math]::Abs($vatE - $vatG) }
    $lineComparisons.Add([PSCustomObject]@{
      Index        = $i
      Label_Extr   = $e.labelRaw
      Label_GT     = $g.labelRaw
      Label_Match  = $labelEq
      Qty_Extr     = [decimal]$e.qty
      Qty_GT       = [decimal]$g.qty
      Qty_Delta    = $qtyDelta
      Unit_Extr    = [decimal]$e.unitPrice
      Unit_GT      = [decimal]$g.unitPrice
      Unit_Delta   = $upDelta
      Total_Extr   = [decimal]$e.lineTotal
      Total_GT     = [decimal]$g.lineTotal
      Total_Delta  = $totDelta
      Vat_Extr     = $vatE
      Vat_GT       = $vatG
      Vat_Delta    = $vatDelta
    })
  }

  if ($extLines.Count -gt $gtLines.Count) {
    Write-Warn ("Righe extra estratte: {0}" -f ($extLines.Count - $gtLines.Count))
  } elseif ($gtLines.Count -gt $extLines.Count) {
    Write-Warn ("Righe mancanti rispetto al GT: {0}" -f ($gtLines.Count - $extLines.Count))
  }

  Write-Host "===== LINE-BY-LINE COMPARISON (first $minCount) =====" -ForegroundColor Green
  $lineComparisons | Format-Table -AutoSize | Out-String | Write-Host

  if ($logsErr -and $logsErr.Count -gt 0) {
    Write-Warn "Marker di errore trovati nei log: $($logsErr -join ', ')"
  }

  # Exit code: 0 se nessun marker errore e sono presenti marker OK
  if (-not $logsErr -and $logsOk -and $logsOk.Count -gt 0) { exit 0 } else { exit 1 }
}
catch {
  Write-Err $_
  exit 2
}
