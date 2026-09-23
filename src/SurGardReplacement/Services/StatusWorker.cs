using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;
using SurGardReplacement.Diagnostics;
using SurGardReplacement.Storage;

namespace SurGardReplacement.Services;

public sealed class StatusWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly HealthState _health;
    private readonly FileSpool _spool;
    private readonly DiagnosticsOptions _options;
    private readonly ILogger<StatusWorker> _logger;

    public StatusWorker(
        HealthState health,
        FileSpool spool,
        IOptions<AppOptions> options,
        ILogger<StatusWorker> logger)
    {
        _health = health;
        _spool = spool;
        _options = options.Value.Diagnostics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = Path.GetFullPath(_options.LogDirectory);
        Directory.CreateDirectory(directory);
        var statusPath = Path.Combine(directory, "status.json");
        var temporaryPath = statusPath + ".tmp";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = _health.Snapshot(_spool.CountPending());
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8, stoppingToken);
                File.Move(temporaryPath, statusPath, overwrite: true);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not update status.json.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.StatusIntervalSeconds), stoppingToken);
        }
    }
}
