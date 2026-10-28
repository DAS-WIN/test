param([string]$Portable = 'artifacts/win-x64', [string]$Output = 'artifacts/screenshots')
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Published startup verification requires Windows.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path (Join-Path $projectRoot $Portable) 'SRBMiner Monitor.exe'
$outRoot = Join-Path $projectRoot $Output
New-Item $outRoot -ItemType Directory -Force | Out-Null
foreach ($mode in @('normal')) {
    $launchArgs = @('--verify-startup', '--no-error-dialog')
    $app = Start-Process -FilePath $exe -ArgumentList $launchArgs -PassThru
    try {
        if (-not $app.WaitForExit(20000)) { throw "Published $mode startup did not complete within 20 seconds." }
        $app.Refresh()
        $logRoot = Join-Path $env:LOCALAPPDATA 'SRBMiner Monitor/Logs'
        $log = Get-ChildItem $logRoot -Filter "startup-*-$($app.Id).log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $log) { throw "Published $mode startup did not produce a startup log. Exit code: $($app.ExitCode)" }
        Copy-Item $log.FullName (Join-Path $outRoot "published-$mode-startup.log")
        if ($app.ExitCode -ne 0 -or -not (Select-String -Path $log.FullName -SimpleMatch 'STARTUP VERIFICATION PASSED')) {
            Get-Content $log.FullName
            throw "Published $mode startup failed. Exit code: $($app.ExitCode)"
        }
        Write-Output "PASS: published executable opened and rendered the $mode window, then exited normally."
    } finally {
        if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
        $app.Dispose()
    }
}
