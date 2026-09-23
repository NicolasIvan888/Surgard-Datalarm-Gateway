[CmdletBinding()]
param(
    [string]$ServiceName = "SurGardReplacement",
    [string]$DataDirectory = "C:\ProgramData\SurGardReplacement",
    [int]$RecentLogLines = 10,
    [int]$MaxStatusAgeSeconds = 30
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Windows Service: $($service.Status)"
}
else {
    Write-Warning "Serviciul '$ServiceName' nu este instalat."
}

$statusPath = Join-Path $DataDirectory "logs\status.json"
if (Test-Path -LiteralPath $statusPath) {
    $statusAge = ((Get-Date) - (Get-Item -LiteralPath $statusPath).LastWriteTime).TotalSeconds
    if ($statusAge -gt $MaxStatusAgeSeconds) {
        Write-Warning "status.json nu a fost actualizat de $([int]$statusAge) secunde."
    }
    Write-Host ""
    Write-Host "Stare operationala:"
    Get-Content -LiteralPath $statusPath -Raw
}
else {
    Write-Warning "status.json nu exista inca: $statusPath"
}

$pendingPath = Join-Path $DataDirectory "spool\pending"
$pending = if (Test-Path -LiteralPath $pendingPath) {
    @(Get-ChildItem -LiteralPath $pendingPath -Filter "*.json" -File).Count
}
else {
    0
}
Write-Host ""
Write-Host "Mesaje in asteptare pe disc: $pending"

$todayLog = Join-Path $DataDirectory ("logs\surguard-{0}.jsonl" -f (Get-Date -Format "yyyy-MM-dd"))
if (Test-Path -LiteralPath $todayLog) {
    Write-Host ""
    Write-Host "Ultimele evenimente:"
    Get-Content -LiteralPath $todayLog -Tail $RecentLogLines
}
