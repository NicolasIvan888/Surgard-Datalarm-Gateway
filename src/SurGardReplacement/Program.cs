using Microsoft.Extensions.Options;
using System.Net;
using SurGardReplacement.Configuration;
using SurGardReplacement.Diagnostics;
using SurGardReplacement.Services;
using SurGardReplacement.Storage;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // A Windows service normally starts with C:\Windows\System32 as its current
    // directory. Always load appsettings.json from beside the executable.
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SurGard Replacement";
});

builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .Validate(options =>
        IPAddress.TryParse(options.Receiver.BindAddress, out _) &&
        options.Receiver.UdpPort is >= 1 and <= 65535 &&
        options.Receiver.TcpPort is >= 1 and <= 65535 &&
        !string.IsNullOrWhiteSpace(options.Receiver.PresetKeyEnvironmentVariable) &&
        !string.IsNullOrWhiteSpace(options.Andromeda.Host) &&
        options.Andromeda.Port is >= 1 and <= 65535 &&
        options.Andromeda.Prefix.Length == 7 &&
        !string.IsNullOrWhiteSpace(options.Storage.SpoolDirectory) &&
        !string.IsNullOrWhiteSpace(options.Storage.BlacklistFile) &&
        !string.IsNullOrWhiteSpace(options.Diagnostics.LogDirectory) &&
        options.Diagnostics.StatusIntervalSeconds is >= 2 and <= 300,
        "Receiver, Andromeda, storage, or diagnostic configuration is invalid.")
    .ValidateOnStart();

builder.Services.AddSingleton<FileSpool>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<HealthState>();
builder.Services.AddSingleton<ObjectBlacklist>();
builder.Services.AddSingleton<MessagePipeline>();
builder.Services.AddHostedService<UdpReceiverWorker>();
builder.Services.AddHostedService<TcpReceiverWorker>();
builder.Services.AddHostedService<AndromedaSenderWorker>();
builder.Services.AddHostedService<StatusWorker>();

var host = builder.Build();

// Force option validation before listeners are started.
_ = host.Services.GetRequiredService<IOptions<AppOptions>>().Value;

await host.RunAsync();
