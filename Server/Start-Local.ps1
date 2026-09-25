param([string]$PythonPath, [string]$GamePath)
$ErrorActionPreference = 'Stop'
$shopDataDirectory = Join-Path $PSScriptRoot 'data'
New-Item -ItemType Directory -Path $shopDataDirectory -Force | Out-Null
if (-not $PythonPath) {
    $shopBundledPython = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    if (Test-Path -LiteralPath $shopBundledPython) { $PythonPath = $shopBundledPython }
    else { $PythonPath = (Get-Command python -ErrorAction Stop).Source }
}
if ($GamePath -and -not (Test-Path -LiteralPath $GamePath -PathType Leaf)) { throw 'GamePath must point to the built game executable.' }
$shopKeyPath = Join-Path $shopDataDirectory 'local-server.key'
if (-not (Test-Path -LiteralPath $shopKeyPath)) {
    $shopRandomBytes = New-Object byte[] 32
    $shopGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $shopGenerator.GetBytes($shopRandomBytes) } finally { $shopGenerator.Dispose() }
    [IO.File]::WriteAllText($shopKeyPath, [BitConverter]::ToString($shopRandomBytes).Replace('-', '').ToLowerInvariant())
}
$shopOldKey = $env:WW_ECONOMY_SERVER_KEY
$shopOldUrl = $env:WW_ECONOMY_URL
$shopOldOutbox = $env:WW_ECONOMY_OUTBOX
$shopGameProcess = $null
try {
    $env:WW_ECONOMY_SERVER_KEY = [IO.File]::ReadAllText($shopKeyPath).Trim()
    $env:WW_ECONOMY_URL = 'http://127.0.0.1:8787'
    $env:WW_ECONOMY_OUTBOX = Join-Path $shopDataDirectory 'outbox'
    if ($GamePath) {
        $shopGameProcess = Start-Process -FilePath (Resolve-Path -LiteralPath $GamePath).Path -ArgumentList '-batchmode', '-nographics', '--ww-dedicated' -WindowStyle Hidden -PassThru
        Write-Host 'Local dedicated match server started.'
    }
    Write-Host 'Local development economy. Keep this terminal open; Ctrl+C stops it.'
    & $PythonPath (Join-Path $PSScriptRoot 'economy.py') --db (Join-Path $shopDataDirectory 'economy.sqlite3')
}
finally {
    if ($shopGameProcess -and -not $shopGameProcess.HasExited) { $shopGameProcess | Stop-Process }
    $env:WW_ECONOMY_SERVER_KEY = $shopOldKey
    $env:WW_ECONOMY_URL = $shopOldUrl
    $env:WW_ECONOMY_OUTBOX = $shopOldOutbox
}
