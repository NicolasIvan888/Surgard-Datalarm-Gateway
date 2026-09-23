[CmdletBinding()]
param(
    [string]$ConfigPath = "C:\Program Files\SurGardReplacement\appsettings.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw "Nu exista fisierul de configurare: $ConfigPath"
}

$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()

$parsedAddress = $null
if (-not [Net.IPAddress]::TryParse([string]$config.Receiver.BindAddress, [ref]$parsedAddress)) {
    $errors.Add("Receiver.BindAddress nu este o adresa IP valida.")
}

foreach ($entry in @(
    @{ Name = "Receiver.UdpPort"; Value = [int]$config.Receiver.UdpPort },
    @{ Name = "Receiver.TcpPort"; Value = [int]$config.Receiver.TcpPort },
    @{ Name = "Andromeda.Port"; Value = [int]$config.Andromeda.Port }
)) {
    if ($entry.Value -lt 1 -or $entry.Value -gt 65535) {
        $errors.Add("$($entry.Name) trebuie sa fie intre 1 si 65535.")
    }
}

if ([string]::IsNullOrWhiteSpace([string]$config.Andromeda.Host)) {
    $errors.Add("Andromeda.Host lipseste.")
}
if ([string]$config.Andromeda.Prefix -notmatch '^.{7}$') {
    $errors.Add("Andromeda.Prefix trebuie sa contina exact 7 caractere.")
}
if ([int]$config.Diagnostics.StatusIntervalSeconds -lt 2) {
    $errors.Add("Diagnostics.StatusIntervalSeconds trebuie sa fie cel putin 2.")
}

$keyVariable = [string]$config.Receiver.PresetKeyEnvironmentVariable
$key = [Environment]::GetEnvironmentVariable($keyVariable, "Machine")
if ($key -notmatch '^[0-9A-Fa-f]{26}$') {
    $errors.Add("Variabila Machine '$keyVariable' lipseste sau nu contine 26 caractere hexazecimale.")
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Configuratia este valida."
Write-Host "Receiver UDP: $($config.Receiver.BindAddress):$($config.Receiver.UdpPort) Enabled=$($config.Receiver.EnableUdp)"
Write-Host "Receiver TCP: $($config.Receiver.BindAddress):$($config.Receiver.TcpPort) Enabled=$($config.Receiver.EnableTcp)"
Write-Host "Andromeda: $($config.Andromeda.Host):$($config.Andromeda.Port)"
Write-Host "Spool: $($config.Storage.SpoolDirectory)"
Write-Host "Log: $($config.Diagnostics.LogDirectory)"
