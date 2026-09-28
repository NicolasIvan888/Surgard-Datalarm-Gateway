using System.Globalization;

namespace SurGardReplacement.Diagnostics;

public static class LogRetention
{
    public static int DeleteExpiredAuditLogs(
        string directory,
        int retentionDays,
        DateTimeOffset utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, 1);

        if (!Directory.Exists(directory))
            return 0;

        // A seven-day retention keeps today's file plus the previous six
        // calendar-day files. Only audit files with a valid date are touched.
        var oldestDateToKeep = DateOnly.FromDateTime(
            utcNow.UtcDateTime.Date.AddDays(-(retentionDays - 1)));
        var deleted = 0;

        foreach (var path in Directory.EnumerateFiles(directory, "surguard-*.jsonl"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var dateText = fileName["surguard-".Length..];
            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var logDate) ||
                logDate >= oldestDateToKeep)
            {
                continue;
            }

            File.Delete(path);
            deleted++;
        }

        return deleted;
    }
}
