[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServiceName = "SurGardReplacement",
    [switch]$RestartService
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

$secureKey = Read-Host "Introduceti cheia de 26 caractere hexazecimale" -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $presetKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}

if ($presetKey -notmatch "^[0-9A-Fa-f]{26}$") {
    throw "Cheia trebuie sa contina exact 26 caractere hexazecimale."
}

if ($PSCmdlet.ShouldProcess("Machine environment", "Actualizare SURGARD_PRESET_KEY")) {
    [Environment]::SetEnvironmentVariable(
        "SURGARD_PRESET_KEY",
        $presetKey.ToUpperInvariant(),
        "Machine")

    Write-Host "Cheia a fost actualizata. Valoarea nu este afisata."
    if ($RestartService) {
        Restart-Service -Name $ServiceName
        Get-Service -Name $ServiceName
    }
    else {
        Write-Host "Cheia intra in vigoare la urmatoarea repornire a serviciului."
    }
}
