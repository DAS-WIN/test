$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_REPOSITORY -or -not $env:GITHUB_SHA) { throw 'Run this publishing step from GitHub Actions.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    [xml]$project = Get-Content src/SrbMinerMonitor.App/SrbMinerMonitor.App.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    $tag = "v$version"
    $publishedTags = @(gh api "repos/$env:GITHUB_REPOSITORY/releases?per_page=100" --paginate --jq '.[].tag_name')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot check existing releases.' }
    if ($publishedTags -contains $tag) {
        Write-Host "Release $tag already exists; verified main without replacing the published assets."
        return
    }
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
    $checks = @($portable, $source, $validation) | ForEach-Object {
        $hash = (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($_))"
    }
    $checks | Set-Content artifacts/SHA256SUMS.txt -Encoding utf8
    gh release create $tag $portable $source $validation artifacts/SHA256SUMS.txt --repo $env:GITHUB_REPOSITORY --target $env:GITHUB_SHA --title "SRBMiner Monitor $version" --notes-file "docs/RELEASE-NOTES-$version.md" --prerelease
    if ($LASTEXITCODE -ne 0) { throw 'GitHub prerelease upload failed.' }
} finally { Pop-Location }
