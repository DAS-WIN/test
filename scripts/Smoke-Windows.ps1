param([string]$Output = 'artifacts/screenshots')
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw '此檢查需要 Windows WPF runtime。' }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $fixture = Join-Path (Get-Location) 'artifacts/fake-miner'
    $gui = Join-Path (Get-Location) 'artifacts/gui-smoke'
    $outputPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Output))
    New-Item $outputPath -ItemType Directory -Force | Out-Null
    dotnet publish tests/SrbMinerMonitor.FakeMiner -c Release -r win-x64 --self-contained false -o $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Integration fixture build failed.' }
    dotnet publish tests/SrbMinerMonitor.WindowsSmoke -c Release -r win-x64 --self-contained false -o $gui
    if ($LASTEXITCODE -ne 0) { throw 'GUI smoke host build failed.' }
    $stdout = Join-Path $outputPath 'gui-host-stdout.log'
    $stderr = Join-Path $outputPath 'gui-host-stderr.log'
    $fixtureExe = Join-Path $fixture 'SrbMinerMonitor.FakeMiner.exe'
    $arguments = ('"{0}" "{1}"' -f $outputPath, $fixtureExe)
    # Launch the actual Windows GUI subsystem apphost, with no attached console.
    $app = Start-Process (Join-Path $gui 'SrbMinerMonitor.WindowsSmoke.exe') -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    try {
        if (-not $app.WaitForExit(360000)) { throw 'GUI integration test exceeded 6 minutes.' }
        $app.Refresh()
        if ($app.ExitCode -ne 0 -or -not (Test-Path (Join-Path $outputPath 'result.txt')) -or -not (Select-String -Path (Join-Path $outputPath 'result.txt') -Pattern '^PASS:')) {
            throw "GUI integration failed or closed prematurely. Exit code: $($app.ExitCode)"
        }
    } finally {
        if (-not $app.HasExited) { $app.Kill($true); $app.WaitForExit() }
        $app.Dispose()
        Get-Content $stdout -ErrorAction SilentlyContinue
        Get-Content $stderr -ErrorAction SilentlyContinue
    }
} finally { Pop-Location }
