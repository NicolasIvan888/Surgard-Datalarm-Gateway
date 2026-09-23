using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;
using SurGardReplacement.Diagnostics;
using SurGardReplacement.Models;
using SurGardReplacement.Storage;

namespace SurGardReplacement.Services;

public sealed class AndromedaSenderWorker : BackgroundService
{
    private const byte ExpectedAcknowledgement = 0x06;
    private const byte FrameTerminator = 0x14;
    private readonly FileSpool _spool;
    private readonly AuditLog _auditLog;
    private readonly HealthState _health;
    private readonly AndromedaOptions _options;
    private readonly ILogger<AndromedaSenderWorker> _logger;

    public AndromedaSenderWorker(
        FileSpool spool,
        AuditLog auditLog,
        HealthState health,
        IOptions<AppOptions> options,
        ILogger<AndromedaSenderWorker> logger)
    {
        _spool = spool;
        _auditLog = auditLog;
        _health = health;
        _options = options.Value.Andromeda;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TcpClient? client = null;
        var reconnectDelay = 1;

        while (!stoppingToken.IsCancellationRequested)
        {
            PendingMessage? message = null;
            try
            {
                message = await _spool.GetNextAsync(stoppingToken);
                if (message is null)
                {
                    if (client is not null && IsDisconnected(client))
                    {
                        client.Dispose();
                        client = null;
                        _health.AndromedaConnected(false);
                        _logger.LogInformation("Andromeda closed the idle connection.");
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                    continue;
                }

                if (client is null || !client.Connected)
                {
                    client?.Dispose();
                    client = new TcpClient();
                    await client.ConnectAsync(_options.Host, _options.Port, stoppingToken);
                    _health.AndromedaConnected(true);
                    reconnectDelay = 1;
                    _logger.LogInformation("Connected to Andromeda at {Host}:{Port}",
                        _options.Host, _options.Port);
                }

                var stream = client.GetStream();
                var frame = BuildFrame(message.Envelope);
                await stream.WriteAsync(frame, stoppingToken);

                using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                ackTimeout.CancelAfter(TimeSpan.FromSeconds(_options.AckTimeoutSeconds));
                var acknowledgement = new byte[1];
                var received = await stream.ReadAsync(acknowledgement, ackTimeout.Token);

                if (received != 1 || acknowledgement[0] != ExpectedAcknowledgement)
                    throw new IOException("Andromeda returned an invalid or missing acknowledgement.");

                _spool.Complete(message);
                _health.AndromedaAcknowledged();
                await _auditLog.WriteAsync("andromeda_acknowledged",
                    new { message.Envelope.Id, message.Envelope.ContactId }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                client?.Dispose();
                client = null;
                _health.AndromedaFailure();
                _logger.LogWarning(exception,
                    "Andromeda delivery failed; pending message remains on disk.");
                await _auditLog.WriteAsync("andromeda_delivery_failed",
                    new { messageId = message?.Envelope.Id, error = exception.Message }, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(reconnectDelay), stoppingToken);
                reconnectDelay = Math.Min(reconnectDelay * 2, _options.MaxReconnectDelaySeconds);
            }
        }

        client?.Dispose();
        _health.AndromedaConnected(false);
    }

    private byte[] BuildFrame(SpoolEnvelope envelope)
    {
        var text = _options.Prefix + envelope.ContactId;
        var frame = new byte[Encoding.ASCII.GetByteCount(text) + 1];
        Encoding.ASCII.GetBytes(text, frame);
        frame[^1] = FrameTerminator;
        return frame;
    }

    private static bool IsDisconnected(TcpClient client)
    {
        try
        {
            var socket = client.Client;
            return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
