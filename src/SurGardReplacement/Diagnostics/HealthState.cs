namespace SurGardReplacement.Diagnostics;

public sealed class HealthState
{
    private long _udpAccepted;
    private long _tcpAccepted;
    private long _rejected;
    private long _duplicates;
    private long _blocked;
    private long _andromedaAcknowledged;
    private long _andromedaFailures;
    private long _lastReceivedUtcTicks;
    private long _lastAcknowledgedUtcTicks;
    private int _andromedaConnected;

    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public void PacketAccepted(string protocol)
    {
        if (string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _udpAccepted);
        else
            Interlocked.Increment(ref _tcpAccepted);

        Interlocked.Exchange(ref _lastReceivedUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    public void PacketRejected() => Interlocked.Increment(ref _rejected);

    public void PacketDuplicate() => Interlocked.Increment(ref _duplicates);

    public void PacketBlocked() => Interlocked.Increment(ref _blocked);

    public void AndromedaConnected(bool connected) =>
        Interlocked.Exchange(ref _andromedaConnected, connected ? 1 : 0);

    public void AndromedaAcknowledged()
    {
        Interlocked.Increment(ref _andromedaAcknowledged);
        Interlocked.Exchange(ref _lastAcknowledgedUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    public void AndromedaFailure()
    {
        Interlocked.Increment(ref _andromedaFailures);
        AndromedaConnected(false);
    }

    public object Snapshot(int pendingMessages) => new
    {
        status = "running",
        generatedAtUtc = DateTimeOffset.UtcNow,
        startedAtUtc = StartedAtUtc,
        receiver = new
        {
            udpAccepted = Interlocked.Read(ref _udpAccepted),
            tcpAccepted = Interlocked.Read(ref _tcpAccepted),
            rejected = Interlocked.Read(ref _rejected),
            duplicates = Interlocked.Read(ref _duplicates),
            blocked = Interlocked.Read(ref _blocked),
            lastReceivedAtUtc = ReadTimestamp(ref _lastReceivedUtcTicks)
        },
        andromeda = new
        {
            connected = Volatile.Read(ref _andromedaConnected) == 1,
            acknowledged = Interlocked.Read(ref _andromedaAcknowledged),
            deliveryFailures = Interlocked.Read(ref _andromedaFailures),
            lastAcknowledgedAtUtc = ReadTimestamp(ref _lastAcknowledgedUtcTicks)
        },
        spool = new
        {
            pendingMessages
        }
    };

    private static DateTimeOffset? ReadTimestamp(ref long storage)
    {
        var ticks = Interlocked.Read(ref storage);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
