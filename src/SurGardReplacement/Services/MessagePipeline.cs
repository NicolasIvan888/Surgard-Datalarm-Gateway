using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;
using SurGardReplacement.Diagnostics;
using SurGardReplacement.Models;
using SurGardReplacement.Protocol;
using SurGardReplacement.Storage;

namespace SurGardReplacement.Services;

public sealed class MessagePipeline
{
    private readonly FileSpool _spool;
    private readonly AuditLog _auditLog;
    private readonly HealthState _health;
    private readonly ObjectBlacklist _blacklist;
    private readonly byte[] _presetKey;
    private readonly int _duplicateWindowSeconds;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentPackets = new();

    public MessagePipeline(
        FileSpool spool,
        AuditLog auditLog,
        HealthState health,
        ObjectBlacklist blacklist,
        IOptions<AppOptions> options)
    {
        _spool = spool;
        _auditLog = auditLog;
        _health = health;
        _blacklist = blacklist;
        _duplicateWindowSeconds = options.Value.Receiver.DuplicateWindowSeconds;

        var variableName = options.Value.Receiver.PresetKeyEnvironmentVariable;
        var keyText = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(keyText))
            throw new InvalidOperationException(
                $"Environment variable '{variableName}' must contain the 26-character transmitter key.");

        _presetKey = DatAlarmDecoder.ParsePresetKey(keyText.Trim());
    }

    public async Task<bool> ProcessAsync(
        string protocol,
        string source,
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken)
    {
        if (packet.Length != DatAlarmDecoder.PacketLength)
        {
            _health.PacketRejected();
            await _auditLog.WriteAsync("packet_rejected_length",
                new { protocol, source, packetLength = packet.Length }, cancellationToken);
            return false;
        }

        var packetBytes = packet.ToArray();
        var packetHash = Convert.ToHexString(SHA256.HashData(packetBytes));

        if (IsDuplicate(packetHash))
        {
            _health.PacketDuplicate();
            await _auditLog.WriteAsync("packet_duplicate",
                new { protocol, source, packetHash }, cancellationToken);
            return true;
        }

        var decoded = DatAlarmDecoder.Decode(packetBytes, _presetKey);
        if (!DatAlarmDecoder.TryNormalizeContactId(decoded, out var normalizedContactId))
        {
            _health.PacketRejected();
            await _auditLog.WriteAsync("packet_rejected_content",
                new { protocol, source, packetHex = Convert.ToHexString(packetBytes) }, cancellationToken);
            return false;
        }

        var envelope = new SpoolEnvelope(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            protocol,
            source,
            Encoding.ASCII.GetString(normalizedContactId),
            Convert.ToHexString(packetBytes));

        if (_blacklist.IsBlocked(envelope.ContactId, out var blockedObject))
        {
            // A valid blocked packet is acknowledged to the transmitter so it
            // does not retry forever, but it never enters the durable spool.
            _health.PacketAccepted(protocol);
            _health.PacketBlocked();
            await _auditLog.WriteAsync("packet_blocked", new
            {
                envelope.Id,
                envelope.ReceivedAtUtc,
                envelope.Protocol,
                envelope.Source,
                envelope.ContactId,
                objectId = blockedObject!.ObjectId,
                reason = blockedObject.Reason,
                envelope.PacketHex
            }, cancellationToken);
            return true;
        }

        // Durable write happens before the device is acknowledged.
        await _spool.EnqueueAsync(envelope, cancellationToken);
        _health.PacketAccepted(protocol);
        await _auditLog.WriteAsync("packet_accepted", envelope, cancellationToken);
        return true;
    }

    private bool IsDuplicate(string packetHash)
    {
        if (_duplicateWindowSeconds <= 0)
            return false;

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddSeconds(-_duplicateWindowSeconds);

        foreach (var item in _recentPackets)
        {
            if (item.Value < cutoff)
                _recentPackets.TryRemove(item.Key, out _);
        }

        if (_recentPackets.TryGetValue(packetHash, out var previous) && previous >= cutoff)
            return true;

        _recentPackets[packetHash] = now;
        return false;
    }
}
