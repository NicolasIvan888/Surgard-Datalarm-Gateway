using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;
using SurGardReplacement.Diagnostics;
using SurGardReplacement.Protocol;
using SurGardReplacement.Services;
using SurGardReplacement.Storage;

var syntheticKey = Convert.FromHexString("000102030405060708090A0B0C");
var packet = Convert.FromHexString("10203040506070EB29AB6D9BAA2AFA5ABA50F0545A");
var expected = "1234E56789012";
var actual = Encoding.ASCII.GetString(DatAlarmDecoder.Decode(packet, syntheticKey));

if (!string.Equals(actual, expected, StringComparison.Ordinal))
    throw new InvalidOperationException($"Decoder mismatch. Expected {expected}, got {actual}.");

if (!DatAlarmDecoder.IsValidContactId(Encoding.ASCII.GetBytes(actual)))
    throw new InvalidOperationException("Valid Contact-ID payload was rejected.");

if (!DatAlarmDecoder.IsValidContactId(Encoding.ASCII.GetBytes("9998R13001003")))
    throw new InvalidOperationException("Valid Contact-ID restoration was rejected.");

if (!DatAlarmDecoder.TryNormalizeContactId(
        Encoding.ASCII.GetBytes("8436140201001"),
        out var numericEvent) ||
    !Encoding.ASCII.GetString(numericEvent).Equals("8436E40201001", StringComparison.Ordinal))
    throw new InvalidOperationException("Numeric Contact-ID event qualifier was not normalized to E.");

if (!DatAlarmDecoder.TryNormalizeContactId(
        Encoding.ASCII.GetBytes("8436340201001"),
        out var numericRestore) ||
    !Encoding.ASCII.GetString(numericRestore).Equals("8436R40201001", StringComparison.Ordinal))
    throw new InvalidOperationException("Numeric Contact-ID restore qualifier was not normalized to R.");

if (!DatAlarmDecoder.TryNormalizeContactId(
        Encoding.ASCII.GetBytes("8436E40201001"),
        out var surGardEvent) ||
    !Encoding.ASCII.GetString(surGardEvent).Equals("8436E40201001", StringComparison.Ordinal))
    throw new InvalidOperationException("Sur-Gard E qualifier changed during normalization.");

if (!DatAlarmDecoder.TryNormalizeContactId(
        Encoding.ASCII.GetBytes("8436R40201001"),
        out var surGardRestore) ||
    !Encoding.ASCII.GetString(surGardRestore).Equals("8436R40201001", StringComparison.Ordinal))
    throw new InvalidOperationException("Sur-Gard R qualifier changed during normalization.");

if (DatAlarmDecoder.IsValidContactId(Encoding.ASCII.GetBytes("1234X56789012")))
    throw new InvalidOperationException("Invalid Contact-ID payload was accepted.");

if (DatAlarmDecoder.IsValidContactId(Encoding.ASCII.GetBytes("1234E56A89012")))
    throw new InvalidOperationException("Non-decimal Contact-ID event fields were accepted.");

if (DatAlarmDecoder.IsValidContactId(Encoding.ASCII.GetBytes("8436640201001")))
    throw new InvalidOperationException("Unmapped Contact-ID status qualifier was accepted.");

var testRoot = Path.Combine(Path.GetTempPath(), "SurGardBlacklist-" + Guid.NewGuid().ToString("N"));
var keyVariable = "SURGARD_TEST_KEY_" + Guid.NewGuid().ToString("N");
try
{
    var blacklistPath = Path.Combine(testRoot, "blacklist.json");
    var spoolPath = Path.Combine(testRoot, "spool");
    var logPath = Path.Combine(testRoot, "logs");
    Directory.CreateDirectory(testRoot);
    await File.WriteAllTextAsync(blacklistPath, JsonSerializer.Serialize(new
    {
        version = 1,
        objects = new[] { new { objectId = "1234", reason = "test", addedAtUtc = DateTimeOffset.UtcNow } }
    }));
    Environment.SetEnvironmentVariable(keyVariable, Convert.ToHexString(syntheticKey));

    var settings = Options.Create(new AppOptions
    {
        Receiver = new ReceiverOptions
        {
            PresetKeyEnvironmentVariable = keyVariable,
            DuplicateWindowSeconds = 0
        },
        Storage = new StorageOptions
        {
            SpoolDirectory = spoolPath,
            BlacklistFile = blacklistPath
        },
        Diagnostics = new DiagnosticsOptions { LogDirectory = logPath }
    });
    var spool = new FileSpool(settings);
    var audit = new AuditLog(settings);
    var health = new HealthState();
    var blacklist = new ObjectBlacklist(settings, NullLogger<ObjectBlacklist>.Instance);
    var pipeline = new MessagePipeline(spool, audit, health, blacklist, settings);

    var acknowledged = await pipeline.ProcessAsync(
        "UDP", "127.0.0.1:50000", packet, CancellationToken.None);
    if (!acknowledged)
        throw new InvalidOperationException("A blocked valid packet must still be acknowledged.");
    if (spool.CountPending() != 0)
        throw new InvalidOperationException("A blocked packet entered the Andromeda spool.");

    var logText = await File.ReadAllTextAsync(
        Directory.EnumerateFiles(logPath, "surguard-*.jsonl").Single());
    if (!logText.Contains("\"eventType\":\"packet_blocked\"", StringComparison.Ordinal))
        throw new InvalidOperationException("Blocked packet was not audited.");

    var snapshot = JsonSerializer.Serialize(health.Snapshot(spool.CountPending()));
    using (var status = JsonDocument.Parse(snapshot))
    {
        var receiver = status.RootElement.GetProperty("receiver");
        if (receiver.GetProperty("blocked").GetInt64() != 1 ||
            receiver.GetProperty("udpAccepted").GetInt64() != 1)
            throw new InvalidOperationException("Blocked packet counters are incorrect.");
    }

    await File.WriteAllTextAsync(blacklistPath, "{\"version\":1,\"objects\":[]}");
    blacklist.Reload(force: true);
    acknowledged = await pipeline.ProcessAsync(
        "UDP", "127.0.0.1:50001", packet, CancellationToken.None);
    if (!acknowledged || spool.CountPending() != 1)
        throw new InvalidOperationException("Removing an object from the blacklist did not restore delivery.");
}
finally
{
    Environment.SetEnvironmentVariable(keyVariable, null);
    if (Directory.Exists(testRoot))
        Directory.Delete(testRoot, recursive: true);
}

Console.WriteLine("SurGard replacement smoke tests passed, including blacklist ACK/no-forward behavior.");
