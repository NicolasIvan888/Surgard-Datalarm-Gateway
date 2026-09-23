using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;
using SurGardReplacement.Protocol;

namespace SurGardReplacement.Services;

public sealed class TcpReceiverWorker : BackgroundService
{
    private static readonly byte[] Acknowledgement = [0x06];
    private readonly MessagePipeline _pipeline;
    private readonly ReceiverOptions _options;
    private readonly ILogger<TcpReceiverWorker> _logger;
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private int _clientId;

    public TcpReceiverWorker(
        MessagePipeline pipeline,
        IOptions<AppOptions> options,
        ILogger<TcpReceiverWorker> logger)
    {
        _pipeline = pipeline;
        _options = options.Value.Receiver;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableTcp)
            return;

        var bindAddress = IPAddress.Parse(_options.BindAddress);
        var listener = new TcpListener(bindAddress, _options.TcpPort);
        listener.Start();
        _logger.LogInformation("TCP listener started on {Address}:{Port}", bindAddress, _options.TcpPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                var id = Interlocked.Increment(ref _clientId);
                var task = HandleClientAsync(client, stoppingToken);
                _clients[id] = task;
                _ = task.ContinueWith(
                    completedTask =>
                    {
                        _clients.TryRemove(id, out var ignored);
                        _ = completedTask.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            listener.Stop();
            await Task.WhenAll(_clients.Values);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var source = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            _logger.LogInformation("TCP device connected: {Source}", source);
            var stream = client.GetStream();
            var packet = new byte[DatAlarmDecoder.PacketLength];

            try
            {
                while (await ReadExactlyAsync(stream, packet, cancellationToken))
                {
                    var accepted = await _pipeline.ProcessAsync(
                        "TCP", source, packet, cancellationToken);

                    if (accepted)
                        await stream.WriteAsync(Acknowledgement, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal service shutdown.
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "TCP device connection failed: {Source}", source);
            }
            finally
            {
                _logger.LogInformation("TCP device disconnected: {Source}", source);
            }
        }
    }

    private static async Task<bool> ReadExactlyAsync(
        NetworkStream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = await stream.ReadAsync(destination[read..], cancellationToken);
            if (count == 0)
                return false;
            read += count;
        }

        return true;
    }
}
