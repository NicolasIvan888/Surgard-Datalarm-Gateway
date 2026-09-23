[CmdletBinding()]
param(
    [int]$ReceiverPort = 11003,
    [int]$MockAndromedaPort = 11004,
    [string]$ContactId = "0000E00000000",
    [string]$Prefix = "DEMO 00"
)

$ErrorActionPreference = "Stop"

if ($ContactId -notmatch "^[0-9A-F]{4}[ER][0-9]{8}$") {
    throw "ContactId trebuie sa aiba formatul AAAA[E/R]EEEPPZZZ."
}
if ($Prefix.Length -ne 7) {
    throw "Prefix trebuie sa contina exact 7 caractere."
}

$keyText = [Environment]::GetEnvironmentVariable("SURGARD_PRESET_KEY", "Process")
if ($keyText -notmatch "^[0-9A-Fa-f]{26}$") {
    $keyText = [Environment]::GetEnvironmentVariable("SURGARD_PRESET_KEY", "Machine")
}
if ($keyText -notmatch "^[0-9A-Fa-f]{26}$") {
    throw "SURGARD_PRESET_KEY lipseste sau este invalida la nivel Machine."
}

$presetKey = New-Object byte[] 13
for ($index = 0; $index -lt $presetKey.Length; $index++) {
    $presetKey[$index] = [Convert]::ToByte($keyText.Substring($index * 2, 2), 16)
}

$packet = New-Object byte[] 21
$randomPrefix = [byte[]](0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70)
[Array]::Copy($randomPrefix, 0, $packet, 0, 7)
$packet[20] = 0x5A

$randomKey = New-Object byte[] 13
[Array]::Copy($packet, 0, $randomKey, 0, 7)
[Array]::Copy($packet, 0, $randomKey, 7, 6)
$plain = [Text.Encoding]::ASCII.GetBytes($ContactId)
$rolling = [int]$packet[20]

for ($index = 0; $index -lt 13; $index++) {
    $rolling = ($rolling + [int]$randomKey[6]) -band 0xFF
    $encryptionKey = ([int]$randomKey[$index] + [int]$presetKey[$index]) -band 0xFF
    $packet[7 + $index] = [byte](
        [int]$plain[$index] -bxor $encryptionKey -bxor $rolling)
}

$listener = [Net.Sockets.TcpListener]::new(
    [Net.IPAddress]::Loopback,
    $MockAndromedaPort)
$listener.Start()
$acceptTask = $listener.AcceptTcpClientAsync()

try {
    $udp = [Net.Sockets.UdpClient]::new()
    try {
        $udp.Client.ReceiveTimeout = 5000
        [void]$udp.Send($packet, $packet.Length, "127.0.0.1", $ReceiverPort)
        $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
        $deviceAcknowledgement = $udp.Receive([ref]$remote)
    }
    finally {
        $udp.Dispose()
    }

    if ($deviceAcknowledgement.Length -ne 1 -or $deviceAcknowledgement[0] -ne 6) {
        throw "Serviciul nu a confirmat pachetul de test cu 0x06."
    }

    if (-not $acceptTask.Wait(10000)) {
        throw "Serviciul nu s-a conectat la Andromeda simulata."
    }

    $andromedaClient = $acceptTask.Result
    try {
        $stream = $andromedaClient.GetStream()
        $stream.ReadTimeout = 5000
        $frame = New-Object byte[] 21
        $offset = 0
        while ($offset -lt $frame.Length) {
            $read = $stream.Read($frame, $offset, $frame.Length - $offset)
            if ($read -eq 0) {
                throw "Conexiunea s-a inchis inainte de primirea cadrului."
            }
            $offset += $read
        }

        $expected = [Text.Encoding]::ASCII.GetBytes($Prefix + $ContactId)
        for ($index = 0; $index -lt $expected.Length; $index++) {
            if ($frame[$index] -ne $expected[$index]) {
                throw "Cadrul primit de Andromeda simulata nu corespunde."
            }
        }
        if ($frame[20] -ne 0x14) {
            throw "Terminatorul cadrului nu este 0x14."
        }

        $stream.WriteByte(6)
        $stream.Flush()
    }
    finally {
        $andromedaClient.Dispose()
    }

    Write-Host "Semnalul de test a fost primit, decodat si confirmat."
    Write-Host "Contact-ID: $ContactId"
    Write-Host "Verificati randul nou in SurGard Replacement Monitor."
}
finally {
    $listener.Stop()
}
