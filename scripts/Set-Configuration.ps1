[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ConfigPath = "C:\Program Files\SurGardReplacement\appsettings.json",
    [ValidateNotNullOrEmpty()]
    [string]$BindAddress,
    [ValidateRange(1, 65535)]
    [int]$UdpPort,
    [ValidateRange(1, 65535)]
    [int]$TcpPort,
    [Nullable[bool]]$EnableUdp,
    [Nullable[bool]]$EnableTcp,
    [ValidateRange(0, 3600)]
    [int]$DuplicateWindowSeconds,
    [ValidateNotNullOrEmpty()]
    [string]$AndromedaHost,
    [ValidateRange(1, 65535)]
    [int]$AndromedaPort,
    [ValidateLength(7, 7)]
    [string]$Prefix,
    [ValidateRange(1, 300)]
    [int]$AckTimeoutSeconds,
    [ValidateRange(1, 300)]
    [int]$MaxReconnectDelaySeconds,
    [string]$SpoolDirectory,
    [string]$LogDirectory,
    [ValidateRange(2, 300)]
    [int]$StatusIntervalSeconds,
    [switch]$RestartService,
    [string]$ServiceName = "SurGardReplacement",
    [string]$DisplayName = "SurGard DataAlarm Replacement"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw "Nu exista fisierul de configurare: $ConfigPath"
}

$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$bound = $PSBoundParameters
$oldUdpPort = [int]$config.Receiver.UdpPort
$oldTcpPort = [int]$config.Receiver.TcpPort

if ($bound.ContainsKey("BindAddress")) { $config.Receiver.BindAddress = $BindAddress }
if ($bound.ContainsKey("UdpPort")) { $config.Receiver.UdpPort = $UdpPort }
if ($bound.ContainsKey("TcpPort")) { $config.Receiver.TcpPort = $TcpPort }
if ($bound.ContainsKey("EnableUdp")) { $config.Receiver.EnableUdp = [bool]$EnableUdp }
if ($bound.ContainsKey("EnableTcp")) { $config.Receiver.EnableTcp = [bool]$EnableTcp }
if ($bound.ContainsKey("DuplicateWindowSeconds")) {
    $config.Receiver.DuplicateWindowSeconds = $DuplicateWindowSeconds
}
if ($bound.ContainsKey("AndromedaHost")) { $config.Andromeda.Host = $AndromedaHost }
if ($bound.ContainsKey("AndromedaPort")) { $config.Andromeda.Port = $AndromedaPort }
if ($bound.ContainsKey("Prefix")) { $config.Andromeda.Prefix = $Prefix }
if ($bound.ContainsKey("AckTimeoutSeconds")) {
    $config.Andromeda.AckTimeoutSeconds = $AckTimeoutSeconds
}
if ($bound.ContainsKey("MaxReconnectDelaySeconds")) {
    $config.Andromeda.MaxReconnectDelaySeconds = $MaxReconnectDelaySeconds
}
if ($bound.ContainsKey("SpoolDirectory")) { $config.Storage.SpoolDirectory = $SpoolDirectory }
if ($bound.ContainsKey("LogDirectory")) { $config.Diagnostics.LogDirectory = $LogDirectory }
if ($bound.ContainsKey("StatusIntervalSeconds")) {
    $config.Diagnostics.StatusIntervalSeconds = $StatusIntervalSeconds
}

$backupPath = "$ConfigPath.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
if ($PSCmdlet.ShouldProcess($ConfigPath, "Actualizare configuratie si creare copie de siguranta")) {
    Copy-Item -LiteralPath $ConfigPath -Destination $backupPath
    $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
    Write-Host "Configuratia a fost actualizata."
    Write-Host "Copie de siguranta: $backupPath"

    $receiverPortsChanged =
        ($bound.ContainsKey("UdpPort") -and $UdpPort -ne $oldUdpPort) -or
        ($bound.ContainsKey("TcpPort") -and $TcpPort -ne $oldTcpPort)
    if ($receiverPortsChanged) {
        $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
        if ($serviceInfo) {
            $programPath = ([string]$serviceInfo.PathName).Trim().Trim('"')
            Get-NetFirewallRule -DisplayName "$DisplayName TCP *" -ErrorAction SilentlyContinue |
                Remove-NetFirewallRule
            Get-NetFirewallRule -DisplayName "$DisplayName UDP *" -ErrorAction SilentlyContinue |
                Remove-NetFirewallRule
            New-NetFirewallRule -DisplayName "$DisplayName TCP $($config.Receiver.TcpPort)" `
                -Direction Inbound -Action Allow -Protocol TCP -LocalPort $config.Receiver.TcpPort `
                -Program $programPath -Profile Domain,Private | Out-Null
            New-NetFirewallRule -DisplayName "$DisplayName UDP $($config.Receiver.UdpPort)" `
                -Direction Inbound -Action Allow -Protocol UDP -LocalPort $config.Receiver.UdpPort `
                -Program $programPath -Profile Domain,Private | Out-Null
            Write-Host "Regulile Windows Firewall au fost actualizate pentru UDP $($config.Receiver.UdpPort) si TCP $($config.Receiver.TcpPort)."
        }
    }

    if ($RestartService) {
        Restart-Service -Name $ServiceName
        Get-Service -Name $ServiceName
    }
    else {
        Write-Host "Modificarile intra in vigoare la urmatoarea repornire a serviciului."
    }
}
