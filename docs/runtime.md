# Runtime behaviour

[Back to README](../README.md)

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
is meant to be cheap enough (see [resource usage](resource-usage.md)) to
just keep running during a match, unlike the vendor app it replaces.
