# Surgard DataAlarm Gateway

A resilient Windows gateway that receives DataAlarm-compatible messages over
UDP or TCP, decodes them, stores them in a durable disk spool, and forwards
them to a central monitoring system. A separate WinForms monitor provides
real-time operational visibility without controlling the service lifecycle.

> Independent interoperability project. It is not affiliated with, endorsed
> by, or supported by Johnson Controls, DSC, Sur-Gard, or any equipment
> manufacturer. Product names are used only to describe compatibility.

## Why this project exists

Legacy alarm receivers often depend on software that is difficult to operate,
observe, or recover after a network interruption. This project demonstrates a
modern replacement focused on reliable delivery and safe operations:

- independent UDP and TCP listeners;
- acknowledgement only after the message is written to durable storage;
- persistent spool until the upstream system acknowledges delivery;
- reconnect with bounded backoff;
- Windows Service deployment and recovery configuration;
- live desktop monitor, delivery delay, counters, disk status, and blacklist;
- repeatable PowerShell installation, verification, and rollback scripts.

## Architecture

```text
DataAlarm-compatible transmitters
              |  UDP / TCP
              v
      SurGard Replacement Service
      | decode and validate
      | durable file spool
      | controlled acknowledgement
              |  TCP
              v
       Monitoring-system endpoint

      SurGard Replacement Monitor
      reads status and audit files only
```

## Repository layout

```text
src/        Windows service and WinForms monitor
tests/      Integration-style smoke and monitor-history tests
scripts/    Build, install, configure, verify, and uninstall scripts
docs/       Public documentation and sanitized screenshots
```

## Security model

The 26-character hexadecimal preset key is never stored in the repository or
in `appsettings.json`. The service reads it from the machine environment:

```powershell
[Environment]::SetEnvironmentVariable(
  "SURGARD_PRESET_KEY",
  "<26 HEXADECIMAL CHARACTERS>",
  "Machine")
```

Do not commit operational keys, production addresses, receiver identifiers,
customer data, logs, packet captures, or production configuration files.

## Development

Requirements:

- Windows 10/11 or Windows Server;
- .NET 10 SDK;
- PowerShell 7 or Windows PowerShell 5.1 for deployment scripts.

Build the complete solution:

```powershell
dotnet build .\Surgard-Datalarm-Gateway.slnx -c Release
```

Run the executable test projects:

```powershell
dotnet run --project .\tests\SurGardReplacement.SmokeTests -c Release
dotnet run --project .\tests\SurGardReplacement.Monitor.HistoryTests -c Release
```

## Safe demonstration defaults

The committed configuration is deliberately isolated:

- receiver: `0.0.0.0:11003` over UDP and TCP;
- upstream endpoint: `127.0.0.1:11004`;
- prefix: `DEMO 00`;
- durable data: `C:\ProgramData\SurGardReplacement`.

Change these values only in an environment-specific file that is excluded from
source control.

## Windows publishing

```powershell
.\scripts\Publish-Windows.ps1
```

The script produces a self-contained Windows x64 build and SHA-256 checksums in
the ignored `artifacts` directory.

## Operational notice

Alarm-processing software is safety-relevant. Test every configuration with a
simulated upstream endpoint and controlled transmitters before production use.
The project is provided without warranty under the MIT License.

## License

Copyright (c) 2026 Ivanov Nicolae. Licensed under the
[MIT License](LICENSE).
