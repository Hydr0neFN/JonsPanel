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
> contact ruanyouyi@hotmail.com and it will be addressed promptly. (Also
> see [CONTACT.md](CONTACT.md).)

# JonsPanel — lightweight native driver for JONSBO case displays

A native, GUI-less program that drives JONSBO USB HID case panels
directly, without the vendor's Electron app. Same wire protocol, far less
CPU/memory: see [Resource usage](#resource-usage) below for measured
numbers against the vendor app it replaces.

## Tested hardware

Verified working, end to end, on exactly this configuration:

- **Panel:** JONSBO case display, USB HID VID `0x5131` / PID `0x2007`
- **Motherboard:** ASUS TUF GAMING B650M-E WIFI
- **CPU:** AMD Ryzen 7 7700
- **GPU:** AMD Radeon RX 9070 XT
- **OS:** Windows 11, build 26200

Anything outside that list is **untested**. Sensor-source names (Super
I/O Vcore/fan labels, GPU vendor sensor names) are resolved by
best-effort name-matching in `HardwareSensors.cs` and differ between
motherboard vendors and Super I/O chips — on a different board you may
need to run `JonsboMonitor.exe --dump-sensors` (see below) and re-point
the relevant `BestOf(...)` call at the real sensor name for that board.
The CPU-fan-vs-case-fan assignment in particular is a "first two fan
sensors reported by Super I/O" heuristic that isn't guaranteed to match
physical headers consistently across boards — see "Known rough edges"
below.

Native, GUI-less replacement for the "JONSBO PC Monitor" Electron app,
which idles at ~3.2% of one core (Electron + 4 child processes +
`SystemInfos.exe`) just to keep the case display updated. This project
talks to the exact same display directly: no Electron, no Chromium, no
extra sensor-polling process.

**Read [PROTOCOL.md](PROTOCOL.md) first if you're going to touch the wire
format.** Short version: the panel is **not** a bitmap/graphical screen and
it is **not** driven over the CH340 serial adapter (COM9) — both of those
were the original starting assumptions for this project and both turned
out to be wrong. It's a small numeric/segment readout (temps, %, RPM,
clocks, date/time) driven over raw **USB HID** (VID `0x5131` / PID
`0x2007`), one 64-byte output report at a time. Anyone extending this later:
don't reintroduce a framebuffer/image pipeline, there's nothing on the
other end that would use it.

## Status

Protocol fully reverse-engineered from the vendor's own (partially
unminified) `app.asar` source — not guessed, not fuzzed. A Python
prototype (`prototype/test_frame.py`) sent the exact same frame derived
from that source and confirmed the real panel lights up and shows output.
Note the honest limit of that confirmation: it verified the panel *lights
up*, not that every one of the 52 fields lands at the correct byte offset.
Since then the running service has been cross-checked field-by-field
against real Windows perf counters and a `--dump-sensors` diagnostic dump
(every raw LHM sensor name/value on this board — see below), which caught
and fixed two real bugs: CPU VID was reading a bogus ~0.22-0.24V telemetry
sensor instead of the Super I/O's actual Vcore (~1.3V), and RAM clock was
a bare 0 with no WMI fallback. Both are fixed; see "Known rough edges"
below for the reasoning kept in-code.

The .NET 8 SDK (8.0.423) is installed and the C# project builds clean —
`dotnet build -c Release`: 0 warnings, 0 errors.

## Layout

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
WMI calls used by the RAM-clock fallback (see below) don't need per-call
warning suppression.

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

## Runtime behaviour

- Auto-detects the panel by VID `0x5131` / PID `0x2007` — no hardcoded COM
  port, and no dependency on which USB hub/port it's plugged into.
- If the device isn't found (unplugged, or a different JONSBO SKU), it
  logs once and retries every 5 seconds. It never busy-spins.
- Sensor values refresh every 1000ms by default (matches the vendor app's
  own cadence); the HID frame is resent every 200ms by default (also
  matches the vendor app — `getSendIntervalTime()` returns a constant
  `200` in `device_hid.js`).
- `--interval-ms=N` / `--sensor-ms=N` / `--fahrenheit` CLI flags override
  the defaults. **Once the Python prototype confirms whether the panel
  latches the last frame or needs the resend to hold its state**, the
  resend interval can likely be raised well past 200ms (e.g. to 1000ms, or
  removed entirely if it latches) — that's the main remaining lever for
  cutting CPU further, and is exactly the vendor's 200ms keepalive-style
  loop we don't need if the hardware itself holds state.
- Logs to `logs/jonsbo-monitor.log` next to the exe (auto-truncates past
  ~5MB) and mirrors to the console for interactive runs. Built as `WinExe`
  (no `Exe`), so a normal launch (scheduled task or double-click) shows no
  console window at all — the log file is the source of truth either way.
- **Single-instance guard:** on startup the process takes a named mutex
  (`Global\JonsboMonitorAlt`). If another real instance already holds it,
  the new one logs that fact and exits (exit code 2) rather than fighting
  over the same USB device. Pass `--allow-multiple` to skip the check
  (testing only). The `--dump-sensors` and `--bench-seconds` diagnostic
  flags below are exempt from the guard by design — they read sensors
  only, return before the HID device is ever opened, and never write to
  the panel, so they're safe to run alongside the real instance.

**Interaction with `power-profiles\Watch-GameProcess.ps1`:** that script's
`$killProcesses` list kills anything named `JONSBO PC Monitor` (the
vendor's process name) when a watched game starts, on the theory that a
sensor-polling process isn't worth its DPC-latency cost during a match.
This replacement's process is named `JonsboMonitor`, which does **not**
match that entry by design — it isn't added to the kill list here, and
nothing in `power-profiles\` is touched by this project. The replacement
is meant to be cheap enough (see "Resource usage" above) to just keep
running during a match, unlike the vendor app it replaces.

## Resource usage

**Original (JONSBO PC Monitor, Electron):** measured, not estimated —
3,966s of CPU time accumulated over 34.4h of uptime = **3.20% of one core**
continuously, spread across the Electron main process, 4 child processes,
and `SystemInfos.exe`.

**This replacement:** built and running, driving the real panel. Measured
with:

```powershell
Get-Process JonsboMonitor | Select-Object WorkingSet64, PrivateMemorySize64, CPU
```

Original design targets were under 30MB working set and under 0.3% of one
core sustained; the steady-state figures below land close to that on
private working set but higher on total working set (self-contained
.NET runtime floor) and on CPU — see the notes on each below.

**These numbers were measured on a machine under 70-90% CPU load from an
unrelated job for the entire measurement window** (confirmed via
`\Processor Information(_Total)\% Processor Utility`), not an idle
desktop — so they're conservative; an idle machine should do at least as
well.

- **CPU, steady state:** **~0.7% of one core**, down from an initial
  **1.263%** before the optimization pass (per-poll LINQ sensor walks
  replaced with cached `ISensor` references, `Update()` scoped to only
  the hardware nodes actually read, WMI RAM-clock lookup cached instead
  of re-queried every poll). A further fast/slow update-cadence split
  (Motherboard/Super I/O fan+Vcore sensors on a 5s cadence, CPU/GPU/Memory
  still on the 2s default) is implemented but its own CPU delta hasn't
  been benchmarked in isolation yet.
- **Working set, steady state:** a long-run soak (untouched process,
  sampled every 60s) climbed from an initial ~56MB up over roughly 9
  minutes to a plateau, then held flat there for the remainder of the
  run: **~65MB total working set / ~29.5MB private working set**, with no
  further growth. That confirms a one-time startup ramp settling into a
  steady state, not a leak. **Private working set** (the part not
  inflated by shared self-contained-.NET-runtime pages, and the fairer
  number to compare against the original) is the one that matters against
  the <30MB target; total WS includes the self-contained .NET runtime's
  own shared pages, which a single-file self-contained publish can't
  shrink below a certain floor regardless of app-level trimming.
  Framework-dependent publish (relying on the machine's installed .NET
  8.0.29 runtime instead of bundling it) has been built and is a real
  option to shrink total WS further if that number matters more than
  install-simplicity; not yet swapped in for a live comparison.
- **Original (JONSBO PC Monitor, Electron), for reference:** 3.20% of one
  core continuously (see above), no working-set figure collected — the
  process is split across 5+ OS processes so a single comparable number
  isn't meaningful the same way.

## Known rough edges / things to verify once running for real

- `HardwareSensors.cs`'s CPU-fan-vs-case-fan assignment is a best-effort
  "first two fan sensors reported by Super I/O" heuristic — Super I/O chips
  don't reliably label which physical header is which. If it's backwards
  on this board, swap the two indices in `HardwareSensors.Read()` (search
  for `fanRpms[0]` / `fanRpms[1]`), or dump the actual sensor names with
  `JonsboMonitor.exe --dump-sensors` (see below) and hardcode by name.
- GPU sensor names vary by vendor/driver; `BestOf()` tries a few common
  substrings ("gpu core", "d3d 3d", etc.) with a fallback to "whatever's
  first" rather than failing outright.
- **CPU VID, fixed:** the CPU hardware node's own per-core `"Core #N VID"`
  sensors are *not* real voltage on this board — they read a flat
  ~0.22-0.24V regardless of load, confirmed via `--dump-sensors`. The real
  measured Vcore (1.2-1.3V range under load, matching expectations for a
  Ryzen 7 7700) is exposed instead by the Nuvoton Super I/O chip under
  `Motherboard/SubHardware` as `"Vcore"`. `HardwareSensors.Read()` now
  prefers that Super I/O `Vcore` sensor and only falls back to the CPU
  node's (probably-still-wrong) VID sensor if a board has no Super I/O
  Vcore reading at all.
- **RAM clock, fixed:** not all motherboards' Super I/O exposes a memory
  clock sensor via LHM (this one doesn't). Falls back to
  `Win32_PhysicalMemory.ConfiguredClockSpeed` via WMI, which reports the
  DDR data rate in MT/s (6000 for this DDR5-6000 EXPO kit) rather than the
  true/actual clock (half that, 3000MHz — DDR is double-pumped). We report
  the MT/s figure deliberately: that's the number Windows Task Manager and
  most consumer monitoring tools show and label "MHz" for DDR memory, so
  it matches what a user expects to see next to their other tools — same
  bug-compatibility reasoning as the Fahrenheit `TempEnd` quirk in
  `FrameBuilder.cs`. If it still reads 0 on a given board, WMI itself
  didn't report a configured clock speed for any installed stick.
- `JonsboMonitor.exe --dump-sensors` prints every sensor LHM can see
  (hardware / name / type / value) to stdout and to `sensor-dump.txt` next
  to the exe, then exits — the tool used to find the real Vcore/RAM-clock
  sensor names above. Useful any time a field looks wrong on a new board:
  run it, `grep` for the field you're chasing, and re-point the relevant
  `BestOf(...)` call in `HardwareSensors.cs` at the real sensor name.

## License and copyright

This project's own code is [MIT-licensed](LICENSE). It is an independent
reimplementation of a wire protocol observed while interoperating with
JONSBO hardware — no JONSBO source code is included in this repository.
`PROTOCOL.md` describes the observed byte-level behaviour in prose (field
offsets, encodings, known quirks) rather than reproducing the vendor's
JavaScript; `FrameBuilder.cs` reproduces the *behaviour* byte-for-byte
(a legitimate interoperability requirement — the panel firmware expects
exact byte values) using this project's own naming and structure, not a
line-by-line transliteration of the vendor's source. "JONSBO" is used
throughout only to identify the hardware this project is compatible with.
