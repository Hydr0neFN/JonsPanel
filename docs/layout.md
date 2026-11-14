# Repo layout

[Back to README](../README.md)

```
(repo root)
  PROTOCOL.md              byte-level protocol reverse-engineering writeup
                           (interoperability documentation — describes the
                           observed wire format in prose, no vendor source)
  prototype/test_frame.py   Python/hidapi validation script (project-local .venv)
  src/JonsboMonitor/        the actual replacement, C#/.NET 8
    JonsboHidDevice.cs      VID/PID auto-detect + open/write, HidSharp
    HardwareSensors.cs      LibreHardwareMonitorLib -> SensorSnapshot
    FrameBuilder.cs         SensorSnapshot -> 64-byte frame (mirrors device_hid.js exactly)
    Program.cs               retry/backoff loop, CLI flags, logging
  install.ps1 / uninstall.ps1   Scheduled Task management
  nuget.config              keeps `dotnet restore` local to this project folder
```

The top level also holds `CONTACT.md` and `LICENSE`.
