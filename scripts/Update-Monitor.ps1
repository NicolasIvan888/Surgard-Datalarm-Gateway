[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "C:\Program Files\SurGardReplacement",
    [bool]$StartMonitor = $true
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

$source = Join-Path $SourceDirectory "SurGardReplacement.Monitor.exe"
$destination = Join-Path $InstallDirectory "SurGardReplacement.Monitor.exe"
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Nu exista actualizarea monitorului: $source"
}
if (-not (Test-Path -LiteralPath $InstallDirectory -PathType Container)) {
    throw "Directorul de instalare nu exista: $InstallDirectory"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = "$destination.$timestamp.bak"

if ($PSCmdlet.ShouldProcess($destination, "Actualizare SurGard Replacement Monitor")) {
    Get-Process -Name "SurGardReplacement.Monitor" -ErrorAction SilentlyContinue |
        Stop-Process -Force

    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        Copy-Item -LiteralPath $destination -Destination $backup
    }

    Copy-Item -LiteralPath $source -Destination $destination -Force
    $version = (Get-Item -LiteralPath $destination).VersionInfo.ProductVersion

    Write-Host "Monitorul a fost actualizat la versiunea $version."
    if (Test-Path -LiteralPath $backup -PathType Leaf) {
        Write-Host "Copie de siguranta: $backup"
    }

    if ($StartMonitor) {
        Start-Process -FilePath $destination -WorkingDirectory $InstallDirectory
    }
}
