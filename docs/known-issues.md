# Known rough edges / things to verify once running for real

[Back to README](../README.md)

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
