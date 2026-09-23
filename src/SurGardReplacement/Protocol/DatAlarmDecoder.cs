namespace SurGardReplacement.Protocol;

public static class DatAlarmDecoder
{
    public const int PacketLength = 21;
    public const int PresetKeyLength = 13;
    public const int ContactIdLength = 13;

    public static byte[] Decode(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> presetKey)
    {
        if (packet.Length != PacketLength)
            throw new ArgumentException($"Packet must contain exactly {PacketLength} bytes.", nameof(packet));

        if (presetKey.Length != PresetKeyLength)
            throw new ArgumentException($"Preset key must contain exactly {PresetKeyLength} bytes.", nameof(presetKey));

        Span<byte> randomKey = stackalloc byte[PresetKeyLength];
        packet[..7].CopyTo(randomKey);
        packet[..6].CopyTo(randomKey[7..]);

        var decoded = new byte[ContactIdLength];
        byte rollingKey = packet[20];

        for (var i = 0; i < ContactIdLength; i++)
        {
            var encryptionKey = unchecked((byte)(randomKey[i] + presetKey[i]));
            rollingKey = unchecked((byte)(rollingKey + randomKey[6]));
            decoded[i] = (byte)(packet[7 + i] ^ encryptionKey ^ rollingKey);
        }

        return decoded;
    }

    public static byte[] ParsePresetKey(string hexadecimalKey)
    {
        if (hexadecimalKey.Length != PresetKeyLength * 2)
            throw new FormatException("The transmitter key must contain exactly 26 hexadecimal characters.");

        return Convert.FromHexString(hexadecimalKey);
    }

    public static bool TryNormalizeContactId(
        ReadOnlySpan<byte> value,
        out byte[] normalized)
    {
        normalized = [];

        if (value.Length != ContactIdLength)
            return false;

        // DataAlarm transmitters use either the raw Contact-ID qualifier
        // (1 = new event, 3 = restore) or the Sur-Gard computer-interface
        // representation (E = event, R = restore):
        // AAAA Q EEE PP ZZZ
        //   AAAA = hexadecimal account identifier
        //   Q    = 1/E (event) or 3/R (restore)
        //   the remaining event/partition/zone fields are decimal digits.
        for (var index = 0; index < 4; index++)
        {
            var character = value[index];
            if (character is not (>= (byte)'0' and <= (byte)'9') &&
                character is not (>= (byte)'A' and <= (byte)'F'))
                return false;
        }

        var normalizedQualifier = value[4] switch
        {
            (byte)'1' => (byte)'E',
            (byte)'3' => (byte)'R',
            (byte)'E' => (byte)'E',
            (byte)'R' => (byte)'R',
            _ => (byte)0
        };

        if (normalizedQualifier == 0)
            return false;

        for (var index = 5; index < value.Length; index++)
        {
            if (value[index] is not (>= (byte)'0' and <= (byte)'9'))
                return false;
        }

        normalized = value.ToArray();
        normalized[4] = normalizedQualifier;
        return true;
    }

    public static bool IsValidContactId(ReadOnlySpan<byte> value) =>
        TryNormalizeContactId(value, out _);
}
