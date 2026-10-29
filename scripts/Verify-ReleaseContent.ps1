param([string[]]$Archives = @())
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$textExtensions = @('.cs', '.csproj', '.props', '.xaml', '.json', '.md', '.ps1', '.cmd', '.yml', '.slnx', '.txt')
$conversationReferences = '(?i)([t]hread://|[c]hatgpt\.com/(?:c|share)/|[c]hat\.openai\.com/|[g]enerated_images[/\\])'

function Assert-Text([string]$Name, [string]$Value) {
    if ($Value -match $conversationReferences) { throw "Conversation reference found in: $Name" }
}
function Assert-Entry([string]$Name, [bool]$ValidationArchive) {
    $normalized = $Name.Replace('\', '/')
    if ($normalized -match '(^|/)(settings\.json|SRBMiner-Monitor-.+\.bat|[^/]+\.user)$' -or
        $normalized -match '(^|/)\.git(/|$)') { throw "Local data must not be packaged: $Name" }
    if (-not $ValidationArchive -and $normalized -match '\.log$') { throw "Runtime log must not be packaged: $Name" }
    if ($normalized -match '(?i)(^|/)(SRBMiner-MULTI\.exe|SrbMinerMonitor\.(Tests|FakeMiner|WindowsSmoke)\.(exe|dll))$') {
        throw "Miner or test binary must not be packaged: $Name"
    }
}

Push-Location $projectRoot
try {
    $files = @(git ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate project source.' }
    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        Assert-Entry $file $false
        if ([System.IO.Path]::GetExtension($file) -in $textExtensions) {
            Assert-Text $file ([System.IO.File]::ReadAllText((Join-Path $projectRoot $file)))
        }
    }
    foreach ($archivePath in $Archives) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead([System.IO.Path]::GetFullPath($archivePath))
        try {
            $isValidation = [System.IO.Path]::GetFileName($archivePath) -like '*-Windows-validation.zip'
            foreach ($entry in $archive.Entries) {
                Assert-Entry $entry.FullName $isValidation
                if ([System.IO.Path]::GetExtension($entry.FullName) -notin $textExtensions) { continue }
                $reader = [System.IO.StreamReader]::new($entry.Open())
                try { Assert-Text $entry.FullName ($reader.ReadToEnd()) }
                finally { $reader.Dispose() }
            }
        } finally { $archive.Dispose() }
    }
    Write-Output 'PASS: release content checks passed for conversation references, local-data files, and packaged binaries.'
} finally { Pop-Location }
