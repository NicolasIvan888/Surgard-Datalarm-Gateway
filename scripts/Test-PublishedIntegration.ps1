[CmdletBinding()]
param(
    [string]$PublishedDirectory = "",
    [int]$ReceiverPort = 12013,
    [int]$MockAndromedaPort = 12014
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PublishedDirectory)) {
    $packagedApp = Join-Path $repositoryRoot "app"
    if (Test-Path -LiteralPath $packagedApp -PathType Container) {
        $PublishedDirectory = $packagedApp
    }
    else {
        $PublishedDirectory = Join-Path $repositoryRoot "artifacts\SurGardReplacement-win-x64"
    }
}
$runRoot = Join-Path $repositoryRoot ("test-runs\" + [guid]::NewGuid().ToString("N"))
$appDirectory = Join-Path $runRoot "app"
$dataDirectory = Join-Path $runRoot "data"
New-Item -ItemType Directory -Path $appDirectory, $dataDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $PublishedDirectory "*") -Destination $appDirectory -Force

$configPath = Join-Path $appDirectory "appsettings.json"
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.Receiver.BindAddress = "127.0.0.1"
    $config.Receiver.UdpPort = $ReceiverPort
    $config.Receiver.TcpPort = $ReceiverPort
$config.Andromeda.Host = "127.0.0.1"
$config.Andromeda.Port = $MockAndromedaPort
$config.Andromeda.AckTimeoutSeconds = 3
$config.Storage.SpoolDirectory = Join-Path $dataDirectory "spool"
$config.Diagnostics.LogDirectory = Join-Path $dataDirectory "logs"
$config.Diagnostics.StatusIntervalSeconds = 2
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $MockAndromedaPort)
$listener.Start()
$acceptTask = $listener.AcceptTcpClientAsync()

$env:SURGARD_PRESET_KEY = "000102030405060708090A0B0C"
$processInfo = [Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = Join-Path $appDirectory "SurGardReplacement.Service.exe"
$processInfo.WorkingDirectory = $appDirectory
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$process = [Diagnostics.Process]::Start($processInfo)

function New-DatAlarmPacket {
    param(
        [Parameter(Mandatory)]
        [ValidateLength(13, 13)]
        [string]$ContactId,
        [Parameter(Mandatory)]
        [byte]$Seed
    )

    $presetKey = New-Object byte[] 13
    for ($index = 0; $index -lt $presetKey.Length; $index++) {
        $presetKey[$index] = [byte]$index
    }

    $packet = New-Object byte[] 21
    for ($index = 0; $index -lt 7; $index++) {
        $packet[$index] = [byte](($Seed + ($index * 0x10)) -band 0xFF)
    }
    $packet[20] = [byte](0x5A + $Seed)

    $randomKey = New-Object byte[] 13
    [Array]::Copy($packet, 0, $randomKey, 0, 7)
    [Array]::Copy($packet, 0, $randomKey, 7, 6)
    $plain = [Text.Encoding]::ASCII.GetBytes($ContactId)
    $rolling = [int]$packet[20]

    for ($index = 0; $index -lt 13; $index++) {
        $rolling = ($rolling + [int]$randomKey[6]) -band 0xFF
        $encryptionKey =
            ([int]$randomKey[$index] + [int]$presetKey[$index]) -band 0xFF
        $packet[7 + $index] = [byte](
            ([int]$plain[$index] -bxor $encryptionKey -bxor $rolling) -band 0xFF)
    }

    return $packet
}

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $probe = [Net.Sockets.TcpClient]::new()
            $probe.Connect("127.0.0.1", $ReceiverPort)
            $probe.Dispose()
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) {
        throw "Receiver-ul nu a pornit pe portul $ReceiverPort."
    }

    $udpPacket = New-DatAlarmPacket -ContactId "1234156789012" -Seed 0x10
    $tcpPacket = New-DatAlarmPacket -ContactId "9998313001003" -Seed 0x20

    $udp = [Net.Sockets.UdpClient]::new()
    try {
        $udp.Client.ReceiveTimeout = 3000
        [void]$udp.Send($udpPacket, $udpPacket.Length, "127.0.0.1", $ReceiverPort)
        $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
        $udpAcknowledgement = $udp.Receive([ref]$remote)
    }
    finally {
        $udp.Dispose()
    }

    $tcp = [Net.Sockets.TcpClient]::new("127.0.0.1", $ReceiverPort)
    try {
        $stream = $tcp.GetStream()
        $stream.ReadTimeout = 3000
        $stream.Write($tcpPacket, 0, $tcpPacket.Length)
        $tcpAcknowledgement = $stream.ReadByte()
    }
    finally {
        $tcp.Dispose()
    }

    if (-not $acceptTask.Wait(5000)) {
        throw "Serviciul nu s-a conectat la Andromeda simulata."
    }
    $andromedaClient = $acceptTask.Result
    try {
        $andromedaStream = $andromedaClient.GetStream()
        $frames = [System.Collections.Generic.List[string]]::new()
        for ($frameIndex = 0; $frameIndex -lt 2; $frameIndex++) {
            $frame = New-Object byte[] 21
            $offset = 0
            while ($offset -lt $frame.Length) {
                $read = $andromedaStream.Read($frame, $offset, $frame.Length - $offset)
                if ($read -eq 0) {
                    throw "Conexiunea Andromeda s-a inchis prematur."
                }
                $offset += $read
            }
            $frames.Add([BitConverter]::ToString($frame).Replace("-", ""))
            $andromedaStream.WriteByte(6)
            $andromedaStream.Flush()
        }
    }
    finally {
        $andromedaClient.Dispose()
    }

    Start-Sleep -Seconds 3
    $statusPath = Join-Path $dataDirectory "logs\status.json"
    $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    $expectedFrames = @(
        "44454D4F2030303132333445353637383930313214",
        "44454D4F2030303939393852313330303130303314"
    )

    if ($udpAcknowledgement[0] -ne 6 -or $tcpAcknowledgement -ne 6) {
        throw "Confirmarea catre emitator nu este 0x06."
    }
    if ($frames.Count -ne 2 -or
        ($frames | Where-Object { $_ -notin $expectedFrames }) -or
        ($expectedFrames | Where-Object { $_ -notin $frames })) {
        throw "Cadrul transmis catre Andromeda nu corespunde."
    }
    if ($status.receiver.udpAccepted -ne 1 -or
        $status.receiver.tcpAccepted -ne 1 -or
        $status.andromeda.acknowledged -ne 2 -or
        $status.spool.pendingMessages -ne 0 -or
        $status.andromeda.connected -ne $false) {
        throw "Contorii status.json nu corespund rezultatului testului."
    }

    Write-Host "Integration test passed."
    Write-Host "UDP accepted: $($status.receiver.udpAccepted)"
    Write-Host "TCP accepted: $($status.receiver.tcpAccepted)"
    Write-Host "Andromeda ACK: $($status.andromeda.acknowledged)"
    Write-Host "Andromeda connected after mock close: $($status.andromeda.connected)"
    Write-Host "Pending: $($status.spool.pendingMessages)"
    Write-Host "Evidence: $runRoot"
}
finally {
    $listener.Stop()
    if ($process -and -not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
}
