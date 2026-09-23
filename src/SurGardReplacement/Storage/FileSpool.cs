using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;
using SurGardReplacement.Models;

namespace SurGardReplacement.Storage;

public sealed class FileSpool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pendingDirectory;
    private readonly string _temporaryDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSpool(IOptions<AppOptions> options)
    {
        var root = Path.GetFullPath(options.Value.Storage.SpoolDirectory);
        _pendingDirectory = Path.Combine(root, "pending");
        _temporaryDirectory = Path.Combine(root, "temporary");
        Directory.CreateDirectory(_pendingDirectory);
        Directory.CreateDirectory(_temporaryDirectory);
    }

    public async Task EnqueueAsync(SpoolEnvelope envelope, CancellationToken cancellationToken)
    {
        var fileName = $"{envelope.ReceivedAtUtc:yyyyMMddHHmmssfffffff}-{envelope.Id}.json";
        var temporaryPath = Path.Combine(_temporaryDirectory, fileName + ".tmp");
        var pendingPath = Path.Combine(_pendingDirectory, fileName);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOptions));

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        // Rename inside the same volume is atomic. Only fully flushed messages
        // become visible to the sender.
        File.Move(temporaryPath, pendingPath);
    }

    public async Task<PendingMessage?> GetNextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = Directory.EnumerateFiles(_pendingDirectory, "*.json")
                .OrderBy(static path => path, StringComparer.Ordinal)
                .FirstOrDefault();

            if (path is null)
                return null;

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var envelope = JsonSerializer.Deserialize<SpoolEnvelope>(json, JsonOptions)
                ?? throw new InvalidDataException($"Invalid spool message: {path}");
            return new PendingMessage(path, envelope);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Complete(PendingMessage message)
    {
        File.Delete(message.FilePath);
    }

    public int CountPending() => Directory.EnumerateFiles(_pendingDirectory, "*.json").Count();
}
