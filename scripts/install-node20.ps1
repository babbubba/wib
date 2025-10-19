param(
  [string]$Version = "20.17.0",
  [string]$InstallDir = "$env:USERPROFILE\node20"
)

Write-Host "Installing Node.js $Version (x64) to $InstallDir ..."
$ErrorActionPreference = 'Stop'

$zipUrl = "https://nodejs.org/dist/v$Version/node-v$Version-win-x64.zip"
$zipPath = Join-Path $env:TEMP "node-v$Version-win-x64.zip"

Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $InstallDir)

$extracted = Join-Path $InstallDir "node-v$Version-win-x64"
if (Test-Path $extracted) {
  Get-ChildItem $extracted | ForEach-Object { Move-Item $_.FullName -Destination $InstallDir -Force }
  Remove-Item -Recurse -Force $extracted
}

Remove-Item $zipPath -Force

$nodePath = Join-Path $InstallDir "node.exe"
if (Test-Path $nodePath) {
  Write-Host "Node installed at: $nodePath"
  $env:Path = "$InstallDir;" + $env:Path
  & $nodePath -v
  Write-Host "To use in new shells, add $InstallDir to PATH or run:`n  $env:USERPROFILE\node20\node.exe -v"
} else {
  Write-Error "node.exe not found after extraction."
}

