using System.Text.Json;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;

namespace SurGardReplacement.Diagnostics;

public sealed class AuditLog
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AuditLog(IOptions<AppOptions> options)
    {
        _directory = Path.GetFullPath(options.Value.Diagnostics.LogDirectory);
        Directory.CreateDirectory(_directory);
    }

    public async Task WriteAsync(string eventType, object details, CancellationToken cancellationToken)
    {
        try
        {
            var entry = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                eventType,
                details
            });

            var path = Path.Combine(_directory, $"surguard-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(path, entry + Environment.NewLine, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Diagnostics must never interrupt reception or delivery of alarms.
            System.Diagnostics.Trace.WriteLine(
                $"SurGard audit log failure ({eventType}): {exception}");
        }
    }
}
