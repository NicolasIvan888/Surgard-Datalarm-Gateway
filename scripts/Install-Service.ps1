[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PresetKey,
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "C:\Program Files\SurGardReplacement",
    [string]$ServiceName = "SurGardReplacement",
    [string]$DisplayName = "SurGard DataAlarm Replacement",
    [ValidateNotNullOrEmpty()]
    [string]$BindAddress = "0.0.0.0",
    [ValidateRange(1, 65535)]
    [int]$UdpPort = 11003,
    [ValidateRange(1, 65535)]
    [int]$TcpPort = 11003,
    [ValidateNotNullOrEmpty()]
    [string]$AndromedaHost = "127.0.0.1",
    [ValidateRange(1, 65535)]
    [int]$AndromedaPort = 11004,
    [ValidateLength(7, 7)]
    [string]$Prefix = "DEMO 00",
    [switch]$StartService
)

$ErrorActionPreference = "Stop"

# PowerShell uses dynamic scoping. Ignore a same-named variable from the
# caller unless PresetKey was explicitly passed as a script parameter.
if (-not $PSBoundParameters.ContainsKey("PresetKey")) {
    $PresetKey = $null
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $packageRoot = Split-Path -Parent $PSScriptRoot
    $packagedApp = Join-Path $packageRoot "app"
    if (Test-Path -LiteralPath $packagedApp -PathType Container) {
        $SourceDirectory = $packagedApp
    }
    else {
        $SourceDirectory = Join-Path $packageRoot "artifacts\SurGardReplacement-win-x64"
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

$sourceExe = Join-Path $SourceDirectory "SurGardReplacement.Service.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Nu exista executabilul publicat: $sourceExe"
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Serviciul '$ServiceName' exista deja. Folositi mai intai scriptul de dezinstalare."
}

if ($PSCmdlet.ShouldProcess($InstallDirectory, "Instalare serviciu $ServiceName")) {
    if ([string]::IsNullOrWhiteSpace($PresetKey)) {
        $secureKey = Read-Host "Introduceti cheia de 26 caractere hexazecimale" -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
        try {
            $PresetKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
    }

    if ($PresetKey -notmatch "^[0-9A-Fa-f]{26}$") {
        throw "Cheia trebuie sa contina exact 26 caractere hexazecimale."
    }

    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory "*") -Destination $InstallDirectory -Recurse -Force

    $configPath = Join-Path $InstallDirectory "appsettings.json"
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.Receiver.BindAddress = $BindAddress
    $config.Receiver.UdpPort = $UdpPort
    $config.Receiver.TcpPort = $TcpPort
    $config.Andromeda.Host = $AndromedaHost
    $config.Andromeda.Port = $AndromedaPort
    $config.Andromeda.Prefix = $Prefix
    $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8

    [Environment]::SetEnvironmentVariable("SURGARD_PRESET_KEY", $PresetKey.ToUpperInvariant(), "Machine")

    $installedExe = Join-Path $InstallDirectory "SurGardReplacement.Service.exe"
    New-Service `
        -Name $ServiceName `
        -BinaryPathName "`"$installedExe`"" `
        -DisplayName $DisplayName `
        -Description "Receives DataAlarm UDP/TCP packets, spools them durably, and forwards them to Andromeda." `
        -StartupType Automatic

    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
    & sc.exe failureflag $ServiceName 1 | Out-Null

    New-NetFirewallRule `
        -DisplayName "$DisplayName TCP $TcpPort" `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort $TcpPort `
        -Program $installedExe -Profile Domain,Private | Out-Null

    New-NetFirewallRule `
        -DisplayName "$DisplayName UDP $UdpPort" `
        -Direction Inbound -Action Allow -Protocol UDP -LocalPort $UdpPort `
        -Program $installedExe -Profile Domain,Private | Out-Null

    if ($StartService) {
        Start-Service -Name $ServiceName
    }
    Get-Service -Name $ServiceName
    Write-Host "Receiver UDP: $BindAddress`:$UdpPort"
    Write-Host "Receiver TCP: $BindAddress`:$TcpPort"
    Write-Host "Andromeda: $AndromedaHost`:$AndromedaPort"
    if (-not $StartService) {
        Write-Host "Serviciul a fost instalat, dar nu a fost pornit."
    }
}
