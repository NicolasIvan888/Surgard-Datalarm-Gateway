[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "C:\Program Files\SurGardReplacement",
    [string]$ServiceName = "SurGardReplacement"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $packageRoot = Split-Path -Parent $PSScriptRoot
    $SourceDirectory = Join-Path $packageRoot "app"
}

$sourceExe = Join-Path $SourceDirectory "SurGardReplacement.Service.exe"
$installedExe = Join-Path $InstallDirectory "SurGardReplacement.Service.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Nu exista actualizarea: $sourceExe"
}
if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
    throw "Serviciul instalat nu a fost gasit: $installedExe"
}
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    throw "Serviciul '$ServiceName' nu este instalat."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupExe = "$installedExe.$timestamp.bak"
$sourcePdb = Join-Path $SourceDirectory "SurGardReplacement.Service.pdb"
$installedPdb = Join-Path $InstallDirectory "SurGardReplacement.Service.pdb"

if ($PSCmdlet.ShouldProcess($installedExe, "Actualizare controlata a serviciului")) {
    Stop-Service -Name $ServiceName -Force
    Copy-Item -LiteralPath $installedExe -Destination $backupExe

    try {
        Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
        if (Test-Path -LiteralPath $sourcePdb -PathType Leaf) {
            Copy-Item -LiteralPath $sourcePdb -Destination $installedPdb -Force
        }
        Start-Service -Name $ServiceName
    }
    catch {
        Copy-Item -LiteralPath $backupExe -Destination $installedExe -Force
        Start-Service -Name $ServiceName
        throw
    }

    Write-Host "Serviciul a fost actualizat."
    Write-Host "Backup executabil: $backupExe"
    Get-Service -Name $ServiceName
}
