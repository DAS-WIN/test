param([string]$Output = 'artifacts/screenshots')
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '此檢查需要 Windows WPF runtime。' }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet run --project tests/SrbMinerMonitor.WindowsSmoke -c Release -- $Output
    if ($LASTEXITCODE -ne 0) { throw 'WPF 煙霧測試失敗，請查閱輸出目錄的 result.txt。' }
} finally { Pop-Location }
