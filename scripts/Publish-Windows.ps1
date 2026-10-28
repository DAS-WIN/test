param([string]$Output = 'artifacts')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Output))
$portable = Join-Path $outRoot 'win-x64'
Push-Location $projectRoot
try {
    dotnet test tests/SrbMinerMonitor.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '核心測試未通過。' }
    dotnet publish src/SrbMinerMonitor.App -c Release -r win-x64 --self-contained true -o $portable
    if ($LASTEXITCODE -ne 0) { throw 'Windows 發布失敗。' }
    Copy-Item README.md $portable
    Copy-Item docs $portable -Recurse -Force
    Copy-Item scripts/Start-Demo.cmd $portable
    Copy-Item scripts/Start-Diagnostics.cmd $portable
    $zip = Join-Path $outRoot 'SRBMinerMonitor-0.1.1-win-x64.zip'
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
    Get-FileHash $zip -Algorithm SHA256
} finally { Pop-Location }
