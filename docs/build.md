# Build and install

[Back to README](../README.md)

## Building

Requires the **.NET 8 SDK** (not just the runtime) — 8.0.423 is installed
on this machine at `C:\Program Files\dotnet\sdk` (via winget,
`Microsoft.DotNet.SDK.8`).

```powershell
cd src\JonsboMonitor
dotnet publish -c Release -r win-x64 --self-contained true
# Output: src\JonsboMonitor\bin\Release\net8.0-windows\win-x64\publish\JonsboMonitor.exe
```

Targets `net8.0-windows`, not plain `net8.0` — this app is Windows-only
anyway (HID device access, elevation manifest, win-x64 RID), and the
`-windows` TFM tags the assembly `SupportedOSPlatform("windows")` so the
WMI calls used by the RAM-clock fallback (see
[known issues](known-issues.md)) don't need per-call warning suppression.

`nuget.config` at the repo root points package restore at `./packages`
inside this project folder rather than the machine-wide NuGet cache.

## Installing

```powershell
# from the repo root, after publishing
.\install.ps1
```

This registers (and immediately starts) a Scheduled Task named
`JonsboMonitorAlt` that launches `JonsboMonitor.exe` at logon, elevated
(required — LibreHardwareMonitorLib needs administrator rights to read
MSR/SMBus/Super I/O sensors) and with no console window.

It deliberately does **not** touch, re-enable, or otherwise interact with
the original `JONSBO_PC_Monitor` scheduled task by default, which stays
disabled. `install.ps1` **asserts** (refuses to install, does not just
warn) if it finds that task still enabled, since running both at once
means two processes fighting over the same USB device. Pass
`-DisableVendorTask` if you want the script to disable it for you instead
of doing that by hand first.

```powershell
.\uninstall.ps1   # removes the JonsboMonitorAlt task and stops the process
```
