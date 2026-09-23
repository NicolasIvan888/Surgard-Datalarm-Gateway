using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;

namespace SurGardReplacement.Services;

public sealed class UdpReceiverWorker : BackgroundService
{
    private static readonly byte[] Acknowledgement = [0x06];
    private readonly MessagePipeline _pipeline;
    private readonly ReceiverOptions _options;
    private readonly ILogger<UdpReceiverWorker> _logger;

    public UdpReceiverWorker(
        MessagePipeline pipeline,
        IOptions<AppOptions> options,
        ILogger<UdpReceiverWorker> logger)
    {
        _pipeline = pipeline;
        _options = options.Value.Receiver;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableUdp)
            return;

        var bindAddress = IPAddress.Parse(_options.BindAddress);
        using var client = new UdpClient(new IPEndPoint(bindAddress, _options.UdpPort));
        _logger.LogInformation("UDP listener started on {Address}:{Port}", bindAddress, _options.UdpPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(stoppingToken);
                var source = result.RemoteEndPoint.ToString();
                var accepted = await _pipeline.ProcessAsync(
                    "UDP", source, result.Buffer, stoppingToken);

                if (accepted)
                    await client.SendAsync(Acknowledgement, result.RemoteEndPoint, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "UDP receive loop failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
