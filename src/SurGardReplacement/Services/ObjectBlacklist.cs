using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SurGardReplacement.Configuration;

namespace SurGardReplacement.Services;

public sealed class ObjectBlacklist
{
    private static readonly Regex ObjectIdPattern = new(
        "^[0-9A-F]{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly ILogger<ObjectBlacklist> _logger;
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, BlacklistEntry> _entries =
        new Dictionary<string, BlacklistEntry>(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private long _lastLength = -1;
    private DateTimeOffset _nextCheckUtc = DateTimeOffset.MinValue;

    public ObjectBlacklist(IOptions<AppOptions> options, ILogger<ObjectBlacklist> logger)
    {
        _path = Path.GetFullPath(options.Value.Storage.BlacklistFile);
        _logger = logger;
        Reload(force: true);
    }

    public bool IsBlocked(string contactId, out BlacklistEntry? entry)
    {
        Reload(force: false);
        entry = null;
        if (contactId.Length < 4)
            return false;

        return _entries.TryGetValue(contactId[..4], out entry);
    }

    public void Reload(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now < _nextCheckUtc)
            return;

        lock (_gate)
        {
            now = DateTimeOffset.UtcNow;
            if (!force && now < _nextCheckUtc)
                return;
            _nextCheckUtc = now.AddSeconds(1);

            try
            {
                if (!File.Exists(_path))
                {
                    if (_lastLength != -1 || force)
                    {
                        _entries = new Dictionary<string, BlacklistEntry>(StringComparer.OrdinalIgnoreCase);
                        _lastWriteUtc = DateTime.MinValue;
                        _lastLength = -1;
                    }
                    return;
                }

                var info = new FileInfo(_path);
                if (!force && info.LastWriteTimeUtc == _lastWriteUtc && info.Length == _lastLength)
                    return;

                var document = JsonSerializer.Deserialize<BlacklistDocument>(
                    File.ReadAllText(_path), JsonOptions)
                    ?? throw new InvalidDataException("Blacklist file is empty.");

                if (document.Version != 1)
                    throw new InvalidDataException($"Unsupported blacklist version: {document.Version}.");

                var loaded = new Dictionary<string, BlacklistEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in document.Objects ?? [])
                {
                    var objectId = item.ObjectId?.Trim().ToUpperInvariant();
                    if (objectId is null || !ObjectIdPattern.IsMatch(objectId))
                        throw new InvalidDataException($"Invalid blacklist object ID: '{item.ObjectId}'.");
                    if (!loaded.TryAdd(objectId, item with { ObjectId = objectId }))
                        throw new InvalidDataException($"Duplicate blacklist object ID: '{objectId}'.");
                }

                _entries = loaded;
                _lastWriteUtc = info.LastWriteTimeUtc;
                _lastLength = info.Length;
                _logger.LogInformation("Loaded {Count} blocked objects from {Path}.", loaded.Count, _path);
            }
            catch (Exception exception)
            {
                // Keep the last valid snapshot. A malformed edit must never
                // accidentally block unrelated alarm traffic.
                _logger.LogWarning(exception,
                    "Could not reload blacklist {Path}; keeping the last valid list.", _path);
            }
        }
    }
}

public sealed record BlacklistDocument(int Version = 1, List<BlacklistEntry>? Objects = null);

public sealed record BlacklistEntry(
    string ObjectId,
    string? Reason = null,
    DateTimeOffset? AddedAtUtc = null);
