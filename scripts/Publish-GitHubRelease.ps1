$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_REPOSITORY -or -not $env:GITHUB_SHA) { throw 'Run this publishing step from GitHub Actions.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    [xml]$project = Get-Content src/SrbMinerMonitor.App/SrbMinerMonitor.App.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    $tag = "v$version"
    $portable = "artifacts/SRBMinerMonitor-$version-win-x64.zip"
    $source = "artifacts/SRBMinerMonitor-$version-source.zip"
    $validation = "artifacts/SRBMinerMonitor-$version-Windows-validation.zip"
    if (-not (Test-Path $portable)) { throw 'Portable package is missing.' }
    foreach ($mode in @('normal')) {
        $log = "artifacts/screenshots/published-$mode-startup.log"
        if (-not (Test-Path $log) -or -not (Select-String -Path $log -SimpleMatch 'STARTUP VERIFICATION PASSED')) {
            throw "Cannot publish: $mode startup verification did not pass."
        }
    }
    git archive --format=zip --prefix=SRBMinerMonitor/ -o $source HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    Compress-Archive -Path artifacts/screenshots/* -DestinationPath $validation -Force
    & ./scripts/Verify-ReleaseContent.ps1 -Archives @($portable, $source, $validation)
    $checks = @($portable, $source, $validation) | ForEach-Object {
        $hash = (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($_))"
    }
    $checks | Set-Content artifacts/SHA256SUMS.txt -Encoding utf8
    gh release create $tag $portable $source $validation artifacts/SHA256SUMS.txt --repo $env:GITHUB_REPOSITORY --target $env:GITHUB_SHA --title "SRBMiner Monitor $version" --notes-file "docs/RELEASE-NOTES-$version.md" --latest
    if ($LASTEXITCODE -ne 0) { throw 'GitHub prerelease upload failed.' }
} finally { Pop-Location }
