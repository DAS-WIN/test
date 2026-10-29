param([string]$Output = 'artifacts')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Output))
$portable = Join-Path $outRoot 'win-x64'
Push-Location $projectRoot
try {
    & ./scripts/Verify-ReleaseContent.ps1
    dotnet test tests/SrbMinerMonitor.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '核心測試未通過。' }
    if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
    dotnet publish src/SrbMinerMonitor.App -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $portable
    if ($LASTEXITCODE -ne 0) { throw 'Windows 發布失敗。' }
    Copy-Item README.md $portable
    Copy-Item docs $portable -Recurse -Force
    Copy-Item scripts/Start-Diagnostics.cmd $portable
    Copy-Item scripts/Start-Real.cmd $portable
    [xml]$project = Get-Content src/SrbMinerMonitor.App/SrbMinerMonitor.App.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    $zip = Join-Path $outRoot "SRBMinerMonitor-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
    & ./scripts/Verify-ReleaseContent.ps1 -Archives @($zip)
    Get-FileHash $zip -Algorithm SHA256
} finally { Pop-Location }
