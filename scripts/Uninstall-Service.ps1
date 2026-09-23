[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServiceName = "SurGardReplacement",
    [string]$DisplayName = "SurGard DataAlarm Replacement",
    [switch]$RemoveData,
    [switch]$RemovePresetKey
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Porniti PowerShell cu Run as administrator."
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Oprire si eliminare serviciu")) {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName -Force
        }
        & sc.exe delete $ServiceName | Out-Null
    }

    Get-NetFirewallRule -DisplayName "$DisplayName TCP *" -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule
    Get-NetFirewallRule -DisplayName "$DisplayName UDP *" -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule

    if ($RemovePresetKey) {
        [Environment]::SetEnvironmentVariable("SURGARD_PRESET_KEY", $null, "Machine")
    }

    if ($RemoveData) {
        $dataPath = "C:\ProgramData\SurGardReplacement"
        if (Test-Path -LiteralPath $dataPath) {
            Remove-Item -LiteralPath $dataPath -Recurse -Force
        }
    }
}
