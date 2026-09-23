[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\SurGardReplacement\SurGardReplacement.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\SurGardReplacement-$Runtime"
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $OutputDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "Publicarea a esuat cu codul $LASTEXITCODE."
}

$hashes = Get-ChildItem -LiteralPath $OutputDirectory -File |
    Get-FileHash -Algorithm SHA256 |
    Select-Object Hash, @{Name = "File"; Expression = { Split-Path -Leaf $_.Path } }

$hashPath = Join-Path $OutputDirectory "SHA256SUMS.txt"
$hashes | ForEach-Object { "$($_.Hash)  $($_.File)" } |
    Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Pachet creat in: $OutputDirectory"
Write-Host "Hash-uri: $hashPath"
