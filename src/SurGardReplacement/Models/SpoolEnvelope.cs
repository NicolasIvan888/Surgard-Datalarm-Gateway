namespace SurGardReplacement.Models;

public sealed record SpoolEnvelope(
    string Id,
    DateTimeOffset ReceivedAtUtc,
    string Protocol,
    string Source,
    string ContactId,
    string PacketHex);

public sealed record PendingMessage(string FilePath, SpoolEnvelope Envelope);
