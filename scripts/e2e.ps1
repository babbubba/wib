Param()
$ErrorActionPreference = 'Stop'

Write-Host "[E2E] Starting stack via docker compose..."
docker compose up -d --build | Out-Null

Write-Host "[E2E] Waiting for API/Proxy/ML..."
for ($i=0; $i -lt 60; $i++) {
  $ok = $false
  try { Invoke-WebRequest -UseBasicParsing http://localhost:8085/ml/health -TimeoutSec 3 | Out-Null; $ok = $true } catch {}
  try { Invoke-WebRequest -UseBasicParsing http://localhost:8080/health/live -TimeoutSec 3 | Out-Null; $ok = $ok -and $true } catch {}
  if ($ok) { break } else { Start-Sleep -Seconds 2 }
}

Write-Host "[E2E] Checking frontends availability..."
function Test-Frontend($url) {
  try {
    $html = (Invoke-WebRequest -UseBasicParsing $url -TimeoutSec 6).Content
    if (-not $html -or $html.Length -lt 100) { throw "empty html" }
    if ($html -notmatch '<app-root') { Write-Warning "missing <app-root> in $url" }
    $js = (Invoke-WebRequest -UseBasicParsing ($url.TrimEnd('/') + '/main.js') -TimeoutSec 6).Content
    if (-not $js -or $js.Length -lt 1000) { throw "main.js too small" }
    return $true
  } catch {
    Write-Error ("Frontend check failed for {0}: {1}" -f $url, $_.Exception.Message)
    return $false
  }
}

$wmcOk = Test-Frontend 'http://localhost:4201/'
$devOk = Test-Frontend 'http://localhost:4200/'
if (-not ($wmcOk -and $devOk)) { throw "Frontend(s) not serving expected assets" }

Write-Host "[E2E] Verifying WMC /api proxy returns JSON (not HTML)"
$body = @{ username = "admin"; password = "admin" } | ConvertTo-Json
try {
  $loginWmc = Invoke-RestMethod -UseBasicParsing -Uri 'http://localhost:4201/api/auth/token' -Method Post -ContentType 'application/json' -Body $body
  if (-not $loginWmc.accessToken) { throw "missing accessToken in response" }
  $wToken = $loginWmc.accessToken
} catch {
  throw "WMC /api/auth/token failed or returned non-JSON: $($_.Exception.Message)"
}

$headers = @{ Authorization = "Bearer $wToken" }
try {
  $from = Get-Date -Format yyyy-MM-01
  $to = Get-Date -Format yyyy-MM-dd
  $spending = Invoke-RestMethod -UseBasicParsing -Uri "http://localhost:4201/api/analytics/spending?from=$from&to=$to" -Headers $headers -Method Get
  if ($spending -is [string]) { throw "received string/HTML instead of JSON" }
} catch {
  throw "WMC /api/analytics/spending failed or returned non-JSON: $($_.Exception.Message)"
}

# Basic category search to ensure JSON via /api
try {
  $cats = Invoke-RestMethod -UseBasicParsing -Uri "http://localhost:4201/api/categories/search?query=lat&take=3" -Headers $headers -Method Get
  if ($cats -is [string]) { throw "received string/HTML instead of JSON" }
} catch {
  Write-Warning "WMC /api/categories/search check skipped/failed: $($_.Exception.Message)"
}

# ML suggestions via /api
try {
  $ml = Invoke-RestMethod -UseBasicParsing -Uri "http://localhost:4201/api/ml/suggestions?labelRaw=latte" -Headers $headers -Method Get
  if ($ml -is [string]) { throw "received string/HTML instead of JSON" }
} catch {
  Write-Warning "WMC /api/ml/suggestions check skipped/failed: $($_.Exception.Message)"
}

Write-Host "[E2E] Auth + protected call"
$from = Get-Date -Format yyyy-MM-01
$to = Get-Date -Format yyyy-MM-dd
$tokenResp = Invoke-WebRequest -UseBasicParsing http://localhost:8085/auth/token -Method Post -ContentType 'application/json' -Body '{"username":"admin","password":"admin"}'
$token = ($tokenResp.Content | ConvertFrom-Json).accessToken
Invoke-WebRequest -UseBasicParsing "http://localhost:8085/analytics/spending?from=$from&to=$to" -Headers @{ Authorization = "Bearer $token" } -Method Get | Out-Null

Write-Host "[E2E] ML suggestions via API"
Invoke-WebRequest -UseBasicParsing "http://localhost:8085/ml/suggestions?labelRaw=latte" -Headers @{ Authorization = "Bearer $token" } -Method Get | Out-Null

Write-Host "[E2E] POST /receipts (expect 202)"
$tmp = New-TemporaryFile
Set-Content -Path $tmp -Value ([byte[]](1..128)) -Encoding Byte
$code = curl.exe -s -o NUL -w "%{http_code}" -F "file=@$tmp;type=application/octet-stream" http://localhost:8085/receipts
if ($code -ne "202") { throw "Upload failed: HTTP $code" }
Write-Host "[E2E] OK - frontends, proxy, API and ML reachable."
