**English** · [繁體中文](README.zh-TW.md)

> **Not affiliated with JONSBO.** This is an independent, unofficial
> reimplementation, not a JONSBO product, and is neither endorsed by nor
> connected to JONSBO or its subsidiaries. "JONSBO" is used descriptively
> only, to identify the hardware this software is compatible with.
>
> It was developed solely to achieve interoperability with a USB HID
> device the author owns, by observing the device's interface. It
> contains no JONSBO source code, binaries, firmware, or assets.
>
> Verified only against the specific hardware listed under
> [Tested hardware](#tested-hardware). Behaviour on any other panel is
> unknown and untested — see the [LICENSE](LICENSE) for the full absence
> of warranty.
>
> **Rights holders:** if you believe anything here infringes your rights,
> see [CONTACT.md](CONTACT.md); a message to the address there will be
> addressed promptly.

# JonsPanel — lightweight native driver for JONSBO case displays

A native, GUI-less program that drives JONSBO USB HID case panels directly,
without the vendor's Electron app. Same wire protocol, far less CPU and
memory: measured numbers are in [At a glance](#at-a-glance) and
[docs/resource-usage.md](docs/resource-usage.md).

## At a glance

| | |
| :-- | :-- |
| **Problem** | The vendor's "JONSBO PC Monitor" Electron app idles at ~3.2% of one core (Electron + 4 child processes + `SystemInfos.exe`) just to keep the case display updated. |
| **What I built** | A native, GUI-less C#/.NET 8 replacement that talks to the same display over USB HID. No Electron, no Chromium, no extra sensor-polling process. |
| **Result** | ~0.7% of one core, down from 1.263% before the optimization pass; ~29.5MB private working set at steady state. |
| **Stack** | C# / .NET 8, LibreHardwareMonitorLib, HidSharp, Windows Scheduled Task |

## Tested hardware

Verified working, end to end, on exactly this configuration:

- **Panel:** JONSBO case display, USB HID VID `0x5131` / PID `0x2007`
- **Motherboard:** ASUS TUF GAMING B650M-E WIFI
- **CPU:** AMD Ryzen 7 7700
- **GPU:** AMD Radeon RX 9070 XT
- **OS:** Windows 11, build 26200

Anything outside that list is **untested**. Sensor-source names (Super I/O
Vcore/fan labels, GPU vendor sensor names) are resolved by best-effort
name-matching in `HardwareSensors.cs` and differ between motherboard vendors
and Super I/O chips. On a different board you may need to run
`JonsboMonitor.exe --dump-sensors` and re-point the relevant `BestOf(...)`
call at the real sensor name. See [Limits and open items](#limits-and-open-items).

## How it works

```
Windows perf counters / LibreHardwareMonitorLib
              |
              v
   HardwareSensors.cs  ->  SensorSnapshot
              |
              v
   FrameBuilder.cs  ->  64-byte HID output report
              |
              v
   JonsboHidDevice.cs  ->  USB HID VID 0x5131 / PID 0x2007
              |
              v
   case display: temps, %, RPM, clocks, date/time
```

The panel is **not** a bitmap/graphical screen, and it is **not** driven over
the CH340 serial adapter (COM9) — both were the original assumptions and both
were wrong. It is a small numeric/segment readout (temps, %, RPM, clocks,
date/time) driven over raw **USB HID** (VID `0x5131` / PID `0x2007`), one
64-byte output report at a time. Sensor values refresh every 1000ms and the
frame is resent every 200ms by default; `--interval-ms=N` / `--sensor-ms=N` /
`--fahrenheit` override the defaults.

Read [PROTOCOL.md](PROTOCOL.md) before you touch the wire format. Do not
reintroduce a framebuffer/image pipeline; there is nothing on the other end
that would use it.

## The engineering story

The first assumptions were wrong. I started from a bitmap screen and the CH340
serial adapter on COM9. Both turned out to be false: the panel is a numeric
segment readout on raw USB HID.

The protocol was reverse-engineered from the vendor's own partially unminified
`app.asar` source — not guessed, not fuzzed. A Python prototype
(`prototype/test_frame.py`) sent the exact same frame and confirmed the real
panel lights up. That confirmation has an honest limit: it verified the panel
*lights up*, not that every one of the 52 fields lands at the correct byte
offset.

Field-by-field cross-checks against real Windows perf counters and a
`--dump-sensors` diagnostic dump found two real bugs. CPU VID was reading a
bogus ~0.22-0.24V telemetry sensor instead of the Super I/O's actual Vcore
(~1.3V). RAM clock was a bare 0 with no WMI fallback. Both are fixed.

What stays open: the CPU-fan-vs-case-fan assignment is a "first two fan
sensors reported by Super I/O" heuristic that is not guaranteed to match
physical headers across boards. Also, once the prototype confirms whether the
panel latches the last frame, the 200ms resend can likely be raised or removed
to cut CPU further.

## Build / run

Requires the **.NET 8 SDK** (not just the runtime); 8.0.423 is installed on
this machine at `C:\Program Files\dotnet\sdk` (via winget,
`Microsoft.DotNet.SDK.8`). The C# project builds clean: `dotnet build -c
Release` gives 0 warnings, 0 errors.

```powershell
cd src\JonsboMonitor
dotnet publish -c Release -r win-x64 --self-contained true
# Output: src\JonsboMonitor\bin\Release\net8.0-windows\win-x64\publish\JonsboMonitor.exe
```

The project targets `net8.0-windows`, not plain `net8.0`; the app is
Windows-only (HID access, elevation manifest, win-x64 RID), and the `-windows`
TFM tags the assembly `SupportedOSPlatform("windows")` so the WMI RAM-clock
fallback needs no per-call warning suppression. `nuget.config` at the repo
root points package restore at `./packages` inside the project folder.

```powershell
# from the repo root, after publishing
.\install.ps1
```

`install.ps1` registers and starts a Scheduled Task named `JonsboMonitorAlt`.
It launches `JonsboMonitor.exe` at logon, elevated (LibreHardwareMonitorLib
needs administrator rights for MSR/SMBus/Super I/O sensors), with no console
window. It refuses to install if the vendor's `JONSBO_PC_Monitor` task is still
enabled, because both processes would fight over the same USB device. Pass
`-DisableVendorTask` to let the script disable it. Full notes:
[docs/build.md](docs/build.md).

```powershell
.\uninstall.ps1   # removes the JonsboMonitorAlt task and stops the process
```

## Repo layout

The tree is small: `src/JonsboMonitor/` holds the C# app, `prototype/test_frame.py`
the hidapi validation script, and `PROTOCOL.md` the protocol writeup. Build and
install scripts sit at the root. See [docs/layout.md](docs/layout.md) for the
full file-by-file layout.

## Limits and open items

- **Fan assignment:** `HardwareSensors.cs` uses "the first two fan sensors
  reported by Super I/O"; Super I/O chips do not reliably label physical
  headers. Swap `fanRpms[0]` / `fanRpms[1]`, or hardcode by name from
  `--dump-sensors`.
- **GPU sensor names** vary by vendor/driver; `BestOf()` tries a few common
  substrings ("gpu core", "d3d 3d") and falls back to the first match.
- **CPU VID (fixed):** now prefers the Super I/O `Vcore` sensor; falls back to
  the CPU node's VID only if no Super I/O Vcore reading exists.
- **RAM clock (fixed):** falls back to `Win32_PhysicalMemory.ConfiguredClockSpeed`
  via WMI. It reports the DDR data rate in MT/s (6000 for this DDR5-6000 EXPO
  kit), not the true 3000MHz clock — deliberately, to match Task Manager and
  most consumer tools.
- **Resend interval:** the 200ms cadence can likely be raised once the
  prototype confirms whether the panel latches the last frame.
- **Untested hardware:** anything outside the list under
  [Tested hardware](#tested-hardware).

Full detail: [docs/known-issues.md](docs/known-issues.md),
[docs/runtime.md](docs/runtime.md),
[docs/resource-usage.md](docs/resource-usage.md).

## License

This project's own code is [MIT-licensed](LICENSE). It is an independent
reimplementation of a wire protocol observed while interoperating with JONSBO
hardware; no JONSBO source code is included in this repository. `PROTOCOL.md`
describes the observed byte-level behaviour in prose. `FrameBuilder.cs`
reproduces the *behaviour* byte-for-byte because the panel firmware expects
exact byte values. "JONSBO" is used only to identify the hardware this project
works with. See [LICENSE](LICENSE).
