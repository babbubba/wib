<# 
.SYNOPSIS
  Shrink di un disco VHD/VHDX (es. ext4.vhdx di Docker Desktop) su Windows 11.

.DESCRIPTION
  - Elevazione solo se necessaria (stop servizi, diskpart/Optimize-VHD).
  - Ferma Docker/WSL, compatta il disco, riavvia Docker.
  - Messaggi colorati + progress bar.
  - Sceglie automaticamente Optimize-VHD (se Hyper-V cmdlet presenti), altrimenti usa diskpart.

.PARAMETER VhdPath
  Path completo al file .vhd/.vhdx (default: %LOCALAPPDATA%\Docker\wsl\data\ext4.vhdx)

.PARAMETER Backup
  Se presente, crea una copia .bak prima della compattazione.

.PARAMETER UseDiskPart
  Forza l’uso di diskpart anche se Optimize-VHD è disponibile.

.PARAMETER NoRestartDocker
  Non riavvia Docker al termine (utile in pipeline di manutenzione).

.EXAMPLE
  .\Shrink-DockerVhd.ps1
  .\Shrink-DockerVhd.ps1 -VhdPath "D:\Docker\ext4.vhdx" -Backup
#>

[CmdletBinding()]
param(
  [Parameter(Position=0)]
#  [string]$VhdPath = (Join-Path $env:LOCALAPPDATA "Docker\wsl\data\ext4.vhdx"),
  [string]$VhdPath = "D:\Users\fabcav\Docker\WSL\DockerDesktopWSL\disk\docker_data.vhdx",
  [switch]$Backup,
  [switch]$UseDiskPart,
  [switch]$NoRestartDocker
)

#----------------------- Utils: console & progress -----------------------
function Write-Ok($msg){ Write-Host "[OK]    $msg" -ForegroundColor Green }
function Write-Info($msg){ Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Write-Warn2($msg){ Write-Host "[WARN]  $msg" -ForegroundColor Yellow }
function Write-Err($msg){ Write-Host "[FAIL]  $msg" -ForegroundColor Red }

function Start-Phase([string]$Activity,[int]$Percent){
  Write-Progress -Activity $Activity -PercentComplete $Percent -Status $Activity
}
function Complete-Phase(){
  Write-Progress -Activity "Completato" -Completed
}

#----------------------- Elevation on-demand -----------------------------
function Test-IsAdmin {
  $current = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($current)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Admin {
  param(
    [string[]]$ArgsToPass
  )
  if(-not (Test-IsAdmin)){
    Write-Info "Sono necessari privilegi elevati: riavvio dello script come Amministratore…"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Process -Id $PID).Path
    $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" " + ($ArgsToPass -join ' ')
    $psi.Verb = "runas"
    try {
      $proc = [System.Diagnostics.Process]::Start($psi)
      exit
    } catch {
      Write-Err "Elevazione rifiutata. Interruzione."
      exit 1
    }
  }
}

#----------------------- Arg quoting helper ------------------------------
function QuoteArg($s){
  if($null -eq $s){ return "" }
  if($s -match '\s' -or $s -match '"'){
    return '"' + ($s -replace '"','`"') + '"'
  }
  return $s
}

#----------------------- Prereq & validation -----------------------------
Write-Host ""
Write-Host "== Shrink VHD/VHDX per Docker/WSL ==" -ForegroundColor Magenta
Write-Host ""

if([string]::IsNullOrWhiteSpace($VhdPath)){
  Write-Err "VhdPath non specificato."
  exit 1
}

#$VhdPath = (Resolve-Path -Path $VhdPath -ErrorAction SilentlyContinue) ?? $VhdPath
$resolved = Resolve-Path -Path $VhdPath -ErrorAction SilentlyContinue; if ($resolved) { $VhdPath = $resolved.Path }

$ext = [IO.Path]::GetExtension($VhdPath).ToLowerInvariant()
if($ext -notin @(".vhd",".vhdx")){
  Write-Warn2 "Estensione inattesa '$ext'. Procedo comunque, ma assicurati sia un VHD/VHDX."
}

if(-not (Test-Path -LiteralPath $VhdPath)){
  Write-Err "File non trovato: $VhdPath"
  exit 1
}

#----------------------- Decide tool (Optimize-VHD vs diskpart) ----------
$optimizeVhdCmd = Get-Command Optimize-VHD -ErrorAction SilentlyContinue
$willUseDiskpart = $UseDiskPart -or (-not $optimizeVhdCmd)
if($willUseDiskpart){
  Write-Info "Userò 'diskpart' per la compattazione."
}else{
  Write-Info "Userò 'Optimize-VHD' (Hyper-V PowerShell) per la compattazione."
}

#----------------------- Build elevation args ----------------------------
# Ricostruisco gli argomenti correnti per l’eventuale riavvio elevato.
$argList = @()
if($PSBoundParameters.ContainsKey('VhdPath')){ $argList += "-VhdPath $(QuoteArg $VhdPath)" }
if($Backup){ $argList += "-Backup" }
if($UseDiskPart){ $argList += "-UseDiskPart" }
if($NoRestartDocker){ $argList += "-NoRestartDocker" }

# Quasi sicuramente serve admin (stop servizi + diskpart/Optimize-VHD)
Ensure-Admin -ArgsToPass $argList

#----------------------- Stop Docker & WSL -------------------------------
Start-Phase "Chiusura Docker & WSL" 5
$dockerWasRunning = $false

try {
  # Docker Desktop Service (se presente)
  $svc = Get-Service -Name "com.docker.service" -ErrorAction SilentlyContinue
  if($svc -and $svc.Status -eq 'Running'){
    $dockerWasRunning = $true
    Write-Info "Arresto servizio Docker Desktop…"
    Stop-Service -Name "com.docker.service" -Force -ErrorAction Stop
    $svc.WaitForStatus('Stopped','00:00:20')
    Write-Ok "Docker Desktop arrestato."
  } else {
    Write-Info "Servizio Docker non in esecuzione (ok)."
  }

  # Chiudo anche eventuali processi Docker Desktop UI
  $ui = Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue
  if($ui){
    Write-Info "Chiusura Docker Desktop UI…"
    $ui | Stop-Process -Force -ErrorAction SilentlyContinue
  }

  # WSL global shutdown (rilascia handle su ext4.vhdx)
  Write-Info "Chiusura WSL (wsl --shutdown)…"
  wsl --shutdown | Out-Null
  Write-Ok "WSL arrestato."
}
catch {
  Write-Warn2 "Problema durante lo stop di Docker/WSL: $($_.Exception.Message)"
}
Complete-Phase

# Breve attesa per rilascio handle
Start-Sleep -Seconds 2

#----------------------- (Opzionale) Backup ------------------------------
if($Backup){
  Start-Phase "Backup del VHDX" 15
  try{
    $bak = "$VhdPath.bak"
    Write-Info "Creo backup: $bak"
    Copy-Item -LiteralPath $VhdPath -Destination $bak -ErrorAction Stop
    Write-Ok "Backup creato."
  } catch {
    Write-Err "Backup fallito: $($_.Exception.Message)"
    Write-Warn2 "Proseguo senza backup su tua responsabilità."
  }
  Complete-Phase
}

#----------------------- Size pre ---------------------------------------
function Get-PrettySize([long]$bytes){
  switch ($bytes) {
    {$_ -ge 1PB} { "{0:N2} PB" -f ($bytes/1PB); break }
    {$_ -ge 1TB} { "{0:N2} TB" -f ($bytes/1TB); break }
    {$_ -ge 1GB} { "{0:N2} GB" -f ($bytes/1GB); break }
    {$_ -ge 1MB} { "{0:N2} MB" -f ($bytes/1MB); break }
    {$_ -ge 1KB} { "{0:N2} KB" -f ($bytes/1KB); break }
    default { "$bytes B" }
  }
}

$sizeBefore = (Get-Item -LiteralPath $VhdPath).Length
Write-Info ("Dimensione prima: " + (Get-PrettySize $sizeBefore))

#----------------------- Compaction -------------------------------------
try{
  Start-Phase "Compattazione del disco" 35

  if(-not $willUseDiskpart){
    # Optimize-VHD richiede Hyper-V PowerShell (funziona anche senza ruolo Hyper-V attivo)
    # -Mode Full tenta la massima riduzione
    Write-Info "Eseguo Optimize-VHD (Mode=Full)…"
    Optimize-VHD -Path $VhdPath -Mode Full -ErrorAction Stop
    Write-Ok "Optimize-VHD completato."
  } else {
    # Fallback: diskpart script temporaneo
    $dp = @"
select vdisk file="$VhdPath"
attach vdisk readonly
compact vdisk
detach vdisk
exit
"@
    $tmp = [IO.Path]::Combine([IO.Path]::GetTempPath(),"compact_vhdx_$(Get-Random).txt")
    $dp | Out-File -FilePath $tmp -Encoding ASCII -Force
    Write-Info "Eseguo diskpart (compact vdisk)…"
    $p = Start-Process -FilePath diskpart.exe -ArgumentList "/s `"$tmp`"" -NoNewWindow -Wait -PassThru
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    if($p.ExitCode -ne 0){
      throw "diskpart ha restituito ExitCode $($p.ExitCode)"
    }
    Write-Ok "diskpart compact completato."
  }

  Complete-Phase
}
catch {
  Complete-Phase
  Write-Err "Compattazione fallita: $($_.Exception.Message)"
  Write-Warn2 "Suggerimenti: assicurati che il file non sia in uso e che tu abbia privilegi amministrativi."
  # Tenta comunque il riavvio di Docker se era attivo
  if($dockerWasRunning -and -not $NoRestartDocker){
    Write-Info "Provo comunque a riavviare Docker…"
    try{ Start-Service -Name "com.docker.service" -ErrorAction Stop; Write-Ok "Docker avviato." }catch{}
  }
  exit 2
}

#----------------------- Size post --------------------------------------
$sizeAfter = (Get-Item -LiteralPath $VhdPath).Length
$delta = $sizeBefore - $sizeAfter

if($delta -gt 0){
  Write-Ok ("Riduzione: " + (Get-PrettySize $delta) + " (nuova dimensione: " + (Get-PrettySize $sizeAfter) + ")")
} else {
  Write-Warn2 ("Nessuna riduzione osservata. Nuova dimensione: " + (Get-PrettySize $sizeAfter) + ".")
}

#----------------------- Restart Docker ---------------------------------
if(-not $NoRestartDocker){
  Start-Phase "Riavvio Docker" 85
  try{
    Write-Info "Avvio servizio Docker Desktop…"
    Start-Service -Name "com.docker.service" -ErrorAction Stop
    # Attendo up a best effort
    Start-Sleep -Seconds 3
    $svc2 = Get-Service -Name "com.docker.service" -ErrorAction SilentlyContinue
    if($svc2 -and $svc2.Status -eq 'Running'){
      Write-Ok "Docker Desktop in esecuzione."
    } else {
      Write-Warn2 "Il servizio Docker non risulta 'Running'. Apri Docker Desktop manualmente se necessario."
    }
  }
  catch{
    Write-Warn2 "Impossibile avviare Docker: $($_.Exception.Message)"
  }
  Complete-Phase
}else{
  Write-Info "Riavvio Docker disabilitato per scelta utente (-NoRestartDocker)."
}

Write-Host ""
Write-Ok "Operazione completata."
Write-Host ""
