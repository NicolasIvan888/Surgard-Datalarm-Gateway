[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "C:\Program Files\SurGardReplacement",
    [switch]$CreateDesktopShortcut
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $packageRoot = Split-Path -Parent $PSScriptRoot
    $SourceDirectory = Join-Path $packageRoot "app"
}

$source = Join-Path $SourceDirectory "SurGardReplacement.Monitor.exe"
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Nu exista monitorul publicat: $source"
}

$destination = Join-Path $InstallDirectory "SurGardReplacement.Monitor.exe"
if ($PSCmdlet.ShouldProcess($destination, "Instalare monitor SurGard")) {
    Copy-Item -LiteralPath $source -Destination $destination -Force

    if ($CreateDesktopShortcut) {
        $desktop = [Environment]::GetFolderPath("CommonDesktopDirectory")
        $shortcutPath = Join-Path $desktop "SurGard Replacement Monitor.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $destination
        $shortcut.WorkingDirectory = $InstallDirectory
        $shortcut.Description = "Monitor in timp real pentru SurGard Replacement"
        $shortcut.Save()
        Write-Host "Shortcut creat: $shortcutPath"
    }

    Write-Host "Monitor instalat: $destination"
}
