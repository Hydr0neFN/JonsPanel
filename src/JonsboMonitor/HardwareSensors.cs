using LibreHardwareMonitor.Hardware;
using System.Management;

namespace JonsboMonitor;

/// <summary>
/// Wraps LibreHardwareMonitorLib's Computer/Hardware/Sensor tree and reduces
/// it to the flat SensorSnapshot the JONSBO frame needs. Replaces the
/// vendor's SystemInfos.exe + shared-memory IPC entirely — this reads
/// sensors in-process, one fewer process than the original.
///
/// Perf note (measured CPU% came in ~4x over the 0.3%-of-a-core target):
/// the LHM tree walk, not the 200ms HID write, is the expensive part of
/// this process — a full Super I/O/SMU read is not cheap. Two things are
/// done here to cut that cost instead of just lowering the poll rate:
///   1. Only the four hardware types actually needed (Cpu/Gpu/Motherboard/
///      Memory) are enabled on the Computer at all — no storage/network/
///      controller/PSU/battery polling, which would otherwise add SMART
///      queries and similar on every pass.
///   2. Sensor <see cref="ISensor"/> references are resolved by name ONCE
///      (in <see cref="ResolveSensors"/>, first call to <see cref="Read"/>
///      only) instead of re-walking hw.Sensors with LINQ/string-Contains
///      matching on every poll. Read() then only calls Update() on the
///      specific IHardware nodes those cached sensors live on, and re-reads
///      .Value off the same ISensor objects.
/// </summary>
public sealed class HardwareSensors : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private bool _resolved;

    // Hardware nodes actually touched on every poll, resolved once so Read()
    // never has to Accept() the whole Computer tree again. Split into two
    // cadences: Cpu/Gpu/Memory update every Read() call (fast — these back
    // fields that visibly move second-to-second: util%, power, clock), while
    // the Motherboard's Super I/O sub-hardware (fan RPM, Vcore — slower-
    // moving, nobody perceives a 5s vs 2s update on a fan gauge) only
    // updates every SlowUpdateMs, tracked by _slowStopwatch below.
    private readonly List<IHardware> _hardwareToUpdate = new();
    private readonly List<IHardware> _slowHardwareToUpdate = new();
    private readonly System.Diagnostics.Stopwatch _slowStopwatch = new();
    private const int SlowUpdateMs = 5000;

    // Cached sensor references — see the class doc above.
    private ISensor? _cpuTempSensor, _cpuUtilSensor, _cpuPowerSensor, _cpuVidFallbackSensor;
    private List<ISensor> _cpuClockSensors = new();
    private ISensor? _gpuTempSensor, _gpuUtilSensor, _gpuPowerSensor, _gpuClockSensor;
    private ISensor? _ramUsedSensor, _ramUsedDataSensor, _ramAvailableDataSensor, _ramClockSensor;
    private ISensor? _motherboardVcoreSensor;
    private readonly List<ISensor> _fanSensors = new();

    // WMI RAM-clock fallback (see GetRamClockFromWmi) is only ever needed
    // once — a stick's configured clock speed doesn't change at runtime —
    // so cache the result instead of re-querying WMI on every poll where
    // the LHM sensor comes back 0. Null = "not looked up yet", not "0".
    private double? _cachedWmiRamClock;

    public HardwareSensors()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            // Everything else (storage/network/controller/PSU/battery) stays
            // off — SMART/NVMe polling in particular is a known expensive
            // LHM subsystem and this panel doesn't display any of it.
        };
        _computer.Open();
    }

    /// <summary>Diagnostic dump of every sensor LHM currently exposes, for
    /// figuring out real sensor names on a given board (e.g. what AMD's
    /// Vcore/VID sensor is actually called here) — see --dump-sensors. Walks
    /// the full tree deliberately (unlike Read()) since this is a one-shot
    /// diagnostic, not the steady-state poll path.</summary>
    public IEnumerable<string> DumpSensors()
    {
        _computer.Accept(_visitor);
        foreach (IHardware hw in _computer.Hardware)
        {
            foreach (ISensor s in hw.Sensors)
                yield return $"{hw.HardwareType}/{hw.Name} :: {s.SensorType} \"{s.Name}\" = {s.Value}";
            foreach (IHardware sub in hw.SubHardware)
                foreach (ISensor s in sub.Sensors)
                    yield return $"{hw.HardwareType}/{hw.Name}/{sub.Name} :: {s.SensorType} \"{s.Name}\" = {s.Value}";
        }
    }

    public SensorSnapshot Read()
    {
        if (!_resolved)
        {
            // One full tree walk so every sensor has a value to name-match
            // against, then pin down the exact ISensor references we care
            // about. Every Read() after this one skips the walk entirely.
            _computer.Accept(_visitor);
            ResolveSensors();
            _resolved = true;
            _slowStopwatch.Start();
        }
        else
        {
            foreach (var hw in _hardwareToUpdate)
                hw.Update();

            if (_slowStopwatch.ElapsedMilliseconds >= SlowUpdateMs)
            {
                foreach (var hw in _slowHardwareToUpdate)
                    hw.Update();
                _slowStopwatch.Restart();
            }
        }

        double cpuTemp = _cpuTempSensor?.Value ?? 0;
        double cpuUtil = _cpuUtilSensor?.Value ?? 0;
        double cpuPower = _cpuPowerSensor?.Value ?? 0;
        double cpuClock = _cpuClockSensors.Count > 0 ? _cpuClockSensors.Max(s => s.Value ?? 0) : 0;
        double cpuVidFallback = _cpuVidFallbackSensor?.Value ?? 0;

        double gpuTemp = _gpuTempSensor?.Value ?? 0;
        double gpuUtil = _gpuUtilSensor?.Value ?? 0;
        double gpuPower = _gpuPowerSensor?.Value ?? 0;
        double gpuClock = _gpuClockSensor?.Value ?? 0;

        double ramUsed = _ramUsedSensor?.Value ?? 0;
        double ramTotal = (_ramUsedDataSensor?.Value ?? 0) + (_ramAvailableDataSensor?.Value ?? 0);
        double ramClock = _ramClockSensor?.Value ?? 0;

        // Super I/O's own Vcore reading is the real measured core voltage
        // (confirmed via --dump-sensors: 1.336V under load here, vs the CPU
        // node's bogus ~0.22-0.24V "Core VID" sensors) — always prefer it
        // over the Cpu-node fallback when present.
        double motherboardVcore = _motherboardVcoreSensor?.Value ?? 0;
        double cpuVid = motherboardVcore > 0 ? motherboardVcore : cpuVidFallback;

        // This board's Super I/O doesn't expose a memory clock sensor via
        // LHM, so fall back to WMI. Win32_PhysicalMemory.ConfiguredClockSpeed
        // reports the JEDEC/XMP-EXPO data rate in MT/s (6000 for this
        // DDR5-6000 EXPO kit) rather than the true/actual clock (half that,
        // 3000MHz, since DDR is double-pumped). We deliberately report the
        // MT/s figure here, matching the number Windows Task Manager and
        // most consumer monitoring tools label "MHz" for DDR memory — same
        // bug-compatibility principle as the Fahrenheit TempEnd quirk in
        // FrameBuilder: match what users expect to see next to their other
        // monitoring tools, not what's technically most correct.
        if (ramClock == 0)
            ramClock = GetCachedRamClockFromWmi();

        // Best-effort fan assignment: first distinct RPM reading is "CPU
        // fan", second is "case fan". Super I/O chips label fan headers
        // generically ("Fan #1", "Fan #2"...) so there is no reliable
        // name-based CPU/case distinction across motherboards — if this
        // doesn't match your board's physical header layout, swap the
        // indices below (or extend ResolveSensors with your fan's exact
        // sensor name once you've identified it via --dump-sensors).
        double cpuFan = 0, caseFan = 0;
        var fanRpms = _fanSensors.Where(s => s.Value is > 0).Select(s => s.Value!.Value).ToList();
        if (fanRpms.Count > 0) cpuFan = fanRpms[0];
        if (fanRpms.Count > 1) caseFan = fanRpms[1];

        return new SensorSnapshot
        {
            CpuTempC = cpuTemp,
            CpuUtilPct = cpuUtil,
            CpuPowerW = cpuPower,
            CpuClockMHz = cpuClock,
            CpuVid = cpuVid,
            CpuFanRpm = cpuFan,
            GpuTempC = gpuTemp,
            GpuUtilPct = gpuUtil,
            GpuPowerW = gpuPower,
            GpuClockMHz = gpuClock,
            CaseFanRpm = caseFan,
            RamUsedPct = ramUsed,
            RamClockMHz = ramClock,
            RamTotalGB = ramTotal,
        };
    }

    /// <summary>Walks the tree exactly once (called right after the first
    /// full Accept()) to pin down the ISensor references Read() will reuse
    /// forever after, and to build the short list of IHardware nodes that
    /// still need Update() calls on every subsequent poll.</summary>
    private void ResolveSensors()
    {
        foreach (IHardware hw in _computer.Hardware)
        {
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    _hardwareToUpdate.Add(hw);
                    _cpuTempSensor = BestOfSensor(hw, SensorType.Temperature, "cpu package", "core (tctl", "core average", "core max");
                    _cpuUtilSensor = BestOfSensor(hw, SensorType.Load, "cpu total");
                    _cpuPowerSensor = BestOfSensor(hw, SensorType.Power, "cpu package", "package");
                    _cpuClockSensors = MatchingSensors(hw, SensorType.Clock, "core");
                    // Fallback only — see the Motherboard/Super I/O "Vcore"
                    // sensor below, which is what actually gets used when
                    // present. On this board (verified via --dump-sensors)
                    // the CPU hardware node's own "Core #N VID" sensors are
                    // NOT real voltage: they read ~0.22-0.24 regardless of
                    // load, while the true Vcore (1.2-1.3V range) is only
                    // exposed by the Nuvoton Super I/O chip under
                    // Motherboard/SubHardware. Keeping this as a last-resort
                    // fallback for boards where the Super I/O doesn't expose
                    // Vcore at all.
                    _cpuVidFallbackSensor = BestOfSensor(hw, SensorType.Voltage, "vcore", "cpu vid", "vid", "core vid", "cpu core");
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    // NOTE: on a multi-GPU board (this one has an iGPU and a
                    // discrete GPU) this keeps the same "last GPU hardware
                    // node enumerated wins" behaviour the pre-caching code
                    // had — not a new bug, just preserved as-is since fixing
                    // GPU selection wasn't part of this pass.
                    _hardwareToUpdate.Add(hw);
                    _gpuTempSensor = BestOfSensor(hw, SensorType.Temperature, "gpu core", "core");
                    _gpuUtilSensor = BestOfSensor(hw, SensorType.Load, "gpu core", "d3d 3d", "core");
                    _gpuPowerSensor = BestOfSensor(hw, SensorType.Power, "gpu package", "gpu power", "package");
                    _gpuClockSensor = BestOfSensor(hw, SensorType.Clock, "gpu core", "core");
                    break;

                case HardwareType.Memory:
                    _hardwareToUpdate.Add(hw);
                    _ramUsedSensor = BestOfSensor(hw, SensorType.Load, "memory");
                    _ramUsedDataSensor = BestOfSensor(hw, SensorType.Data, "memory used");
                    _ramAvailableDataSensor = BestOfSensor(hw, SensorType.Data, "memory available");
                    break;

                case HardwareType.Motherboard:
                    // Motherboard fans/Vcore are reported by the Super I/O
                    // sub-hardware, not the top-level Motherboard node — only
                    // the sub-hardware actually needs updating each poll.
                    foreach (IHardware sub in hw.SubHardware)
                    {
                        _slowHardwareToUpdate.Add(sub);
                        foreach (ISensor s in sub.Sensors)
                        {
                            if (s.SensorType == SensorType.Fan)
                                _fanSensors.Add(s);
                            if (_ramClockSensor is null && s.SensorType == SensorType.Clock &&
                                s.Name.Contains("memory", StringComparison.OrdinalIgnoreCase))
                                _ramClockSensor = s;
                            if (_motherboardVcoreSensor is null && s.SensorType == SensorType.Voltage &&
                                s.Name.Contains("vcore", StringComparison.OrdinalIgnoreCase) &&
                                s.Value is > 0)
                                _motherboardVcoreSensor = s;
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>First sensor of the given type whose name contains one of the
    /// preferred substrings (checked in priority order); falls back to the
    /// first sensor of that type at all if none of the names match. Only
    /// used during ResolveSensors() — steady-state Read() never calls this.</summary>
    private static ISensor? BestOfSensor(IHardware hw, SensorType type, params string[] preferredNameContains)
    {
        var candidates = hw.Sensors.Where(s => s.SensorType == type).ToList();
        foreach (var wanted in preferredNameContains)
        {
            var hit = candidates.FirstOrDefault(s => s.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return candidates.FirstOrDefault();
    }

    /// <summary>All sensors of the given type whose name contains the given
    /// substring — used for CpuClockMHz, which wants the max across every
    /// per-core clock sensor rather than a single "best" one. Only used
    /// during ResolveSensors().</summary>
    private static List<ISensor> MatchingSensors(IHardware hw, SensorType type, string nameContains)
    {
        return hw.Sensors
            .Where(s => s.SensorType == type && s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Cached wrapper around GetRamClockFromWmi — a stick's
    /// configured clock speed can't change at runtime, so querying WMI more
    /// than once per process lifetime is pure waste.</summary>
    private double GetCachedRamClockFromWmi()
    {
        _cachedWmiRamClock ??= GetRamClockFromWmi();
        return _cachedWmiRamClock.Value;
    }

    /// <summary>Reads RAM clock speed from WMI as a fallback when LHM has no
    /// memory clock sensor. Uses the highest ConfiguredClockSpeed across
    /// installed sticks (they should all match on a healthy dual/quad-channel
    /// kit); returns 0 (not the JEDEC unconfigured default) if WMI reports
    /// nothing usable, so the caller can tell "no reading" apart from a
    /// genuine 0.</summary>
    private static double GetRamClockFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            double max = 0;
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["ConfiguredClockSpeed"] is uint speed && speed > max)
                    max = speed;
            }
            return max;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose() => _computer.Close();

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
