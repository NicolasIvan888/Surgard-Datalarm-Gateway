using System.ComponentModel.DataAnnotations;

namespace SurGardReplacement.Configuration;

public sealed class AppOptions
{
    public ReceiverOptions Receiver { get; init; } = new();
    public AndromedaOptions Andromeda { get; init; } = new();
    public StorageOptions Storage { get; init; } = new();
    public DiagnosticsOptions Diagnostics { get; init; } = new();
}

public sealed class ReceiverOptions
{
    [Required]
    public string BindAddress { get; init; } = "0.0.0.0";

    [Range(1, 65535)]
    public int UdpPort { get; init; } = 11003;

    [Range(1, 65535)]
    public int TcpPort { get; init; } = 11003;

    /// <summary>
    /// Name of the environment variable containing the 26-character hex key.
    /// The operational key is deliberately not stored in appsettings.json.
    /// </summary>
    [Required]
    public string PresetKeyEnvironmentVariable { get; init; } = "SURGARD_PRESET_KEY";

    public bool EnableUdp { get; init; } = true;
    public bool EnableTcp { get; init; } = true;

    /// <summary>
    /// Disabled by default: for alarm traffic, an extra duplicate is safer than
    /// discarding a real retransmission.
    /// </summary>
    public int DuplicateWindowSeconds { get; init; } = 0;
}

public sealed class AndromedaOptions
{
    [Required]
    public string Host { get; init; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; init; } = 11004;

    [Required]
    [StringLength(7, MinimumLength = 7)]
    public string Prefix { get; init; } = "DEMO 00";

    [Range(1, 300)]
    public int AckTimeoutSeconds { get; init; } = 10;

    [Range(1, 300)]
    public int MaxReconnectDelaySeconds { get; init; } = 30;
}

public sealed class StorageOptions
{
    [Required]
    public string SpoolDirectory { get; init; } = @"C:\ProgramData\SurGardReplacement\spool";

    [Required]
    public string BlacklistFile { get; init; } = @"C:\ProgramData\SurGardReplacement\blacklist.json";
}

public sealed class DiagnosticsOptions
{
    [Required]
    public string LogDirectory { get; init; } = @"C:\ProgramData\SurGardReplacement\logs";

    [Range(2, 300)]
    public int StatusIntervalSeconds { get; init; } = 10;
}
