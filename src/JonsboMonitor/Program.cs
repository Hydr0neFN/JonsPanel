using JonsboMonitor;

// --- CLI options -------------------------------------------------------
// --interval-ms N   HID resend cadence. Vendor default is 200ms (5x/s).
//                    Once you've confirmed via the Python prototype whether
//                    the panel latches the last frame or blanks/reverts
//                    without a resend, this can likely be raised to 1000ms
//                    (matching the sensor refresh rate) or higher, cutting
//                    CPU further. Kept at the vendor's 200ms by default so
//                    behaviour matches the original out of the box.
// --sensor-ms N      Sensor refresh cadence. Vendor default is 1000ms, but
//                    the LHM tree read (not the HID write) is what's
//                    expensive here — nobody perceives the temp/RPM readout
//                    updating every 1s vs 2s, so this replacement defaults
//                    to 2000ms instead. Still fully configurable if 1000ms
//                    (vendor-matching) is wanted back.
// --fahrenheit       Display Fahrenheit instead of Celsius.
// --log-sensor-ms N  How often the full sensor struct being sent to the
//                    panel gets written to the log, so a couple of fields
//                    (CPU temp, fan RPM) can be eyeballed against HWiNFO /
//                    Task Manager. Separate from --sensor-ms so this can
//                    stay coarse (default 30s) without spamming the log at
//                    the panel's own refresh rate. 0 disables it.
// --dump-sensors     Print every LHM sensor (hardware/name/type/value) once
//                    and exit — for identifying a board's exact sensor
//                    names (e.g. which one is real Vcore/VID) instead of
//                    guessing at name-matching substrings.
// --bench-seconds=N  Loop HardwareSensors.Read() at the --sensor-ms cadence
//                    for N seconds and report CPU time used, then exit —
//                    deliberately does NOT open the HID device (see the
//                    early-return below, which runs before the device is
//                    ever constructed), so this can run side-by-side with a
//                    live instance (e.g. a soak test) without fighting it
//                    for the panel. Used to A/B LHM-side optimizations
//                    (sensor caching, split fast/slow update cadences) in
//                    isolation from device I/O. Exempt from the single-
//                    instance mutex below for the same reason: it's a
//                    read-only diagnostic, not a second panel driver.
// --allow-multiple   Skip the single-instance mutex check (see below).
//                    For testing only — do not use this in the scheduled
//                    task; two real instances both write frames to the
//                    panel and both poll LHM, doubling CPU for nothing.
int intervalMs = 200;
int sensorMs = 2000;
int logSensorMs = 30000;
int benchSeconds = 0;
bool dumpSensors = false;
bool allowMultiple = false;
foreach (var arg in args)
{
    if (arg.StartsWith("--interval-ms=")) intervalMs = int.Parse(arg["--interval-ms=".Length..]);
    else if (arg.StartsWith("--sensor-ms=")) sensorMs = int.Parse(arg["--sensor-ms=".Length..]);
    else if (arg.StartsWith("--log-sensor-ms=")) logSensorMs = int.Parse(arg["--log-sensor-ms=".Length..]);
    else if (arg.StartsWith("--bench-seconds=")) benchSeconds = int.Parse(arg["--bench-seconds=".Length..]);
    else if (arg == "--fahrenheit") FrameBuilder.UseFahrenheit = true;
    else if (arg == "--dump-sensors") dumpSensors = true;
    else if (arg == "--allow-multiple") allowMultiple = true;
}

if (dumpSensors)
{
    using var dumpSensorsSet = new HardwareSensors();
    var lines = dumpSensorsSet.DumpSensors().ToList();
    foreach (var line in lines) Console.WriteLine(line);
    // Elevation (required for sensor access) launches via a UAC prompt that
    // doesn't inherit the parent's stdout, so also write to a fixed file
    // next to the exe — that's the reliable way to retrieve this output.
    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "sensor-dump.txt"), lines);
    return 0;
}

if (benchSeconds > 0)
{
    using var benchSensors = new HardwareSensors();
    var proc = System.Diagnostics.Process.GetCurrentProcess();
    var wall = System.Diagnostics.Stopwatch.StartNew();
    while (wall.Elapsed.TotalSeconds < benchSeconds)
    {
        benchSensors.Read();
        Thread.Sleep(sensorMs);
    }
    proc.Refresh();
    double cpuPct = proc.TotalProcessorTime.TotalSeconds / wall.Elapsed.TotalSeconds * 100;
    string result = $"Bench: {wall.Elapsed.TotalSeconds:F1}s wall, sensorMs={sensorMs}, " +
        $"CPU time={proc.TotalProcessorTime.TotalSeconds:F3}s ({cpuPct:F3}% of one core)";
    Console.WriteLine(result);
    // Same UAC-elevation stdout-capture problem as --dump-sensors.
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bench-result.txt"), result);
    return 0;
}

// Single-instance guard: only the real panel-driving loop below needs this
// (--dump-sensors and --bench-seconds both already returned above and never
// reach here). Without it, a scheduled-task launch at logon plus the user
// double-clicking the exe — or two overlapping scheduled task runs — would
// both open the HID device and both write frames every 200ms, fighting over
// the panel and silently doubling the CPU/LHM cost this whole optimisation
// pass was spent reducing. Global\ (not Local\) so this holds across user
// sessions too, matching how the scheduled task runs.
Mutex? singleInstanceMutex = null;
if (!allowMultiple)
{
    singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Global\JonsboMonitorAlt", createdNew: out bool createdNew);
    if (!createdNew)
    {
        FileLog.Write("Another instance is already running (Global\\JonsboMonitorAlt mutex held). Exiting.");
        return 2;
    }
}

FileLog.Write($"JonsboMonitor starting. interval={intervalMs}ms sensor={sensorMs}ms fahrenheit={FrameBuilder.UseFahrenheit}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

using var sensors = new HardwareSensors();
using var device = new JonsboHidDevice();

byte[] frame = new byte[FrameBuilder.FrameLength];
var sensorStopwatch = System.Diagnostics.Stopwatch.StartNew();
var logStopwatch = System.Diagnostics.Stopwatch.StartNew();
bool haveFrame = false;
bool loggedFirstFrame = false;

const int deviceRetryDelayMs = 5000;

while (!cts.IsCancellationRequested)
{
    if (!device.IsOpen)
    {
        if (!device.TryOpen())
        {
            // Device absent (unplugged, or the exact panel model differs) —
            // log and retry, never spin. This is the "graceful if device
            // absent" requirement.
            FileLog.Write("Panel not found (VID 0x5131 / PID 0x2007). Retrying in 5s.");
            try { await Task.Delay(deviceRetryDelayMs, cts.Token); }
            catch (TaskCanceledException) { break; }
            continue;
        }
        FileLog.Write("Panel connected.");
        sensorStopwatch.Restart();
        haveFrame = false;
    }

    if (!haveFrame || sensorStopwatch.ElapsedMilliseconds >= sensorMs)
    {
        var snapshot = sensors.Read();
        frame = FrameBuilder.Build(snapshot, DateTime.Now);
        sensorStopwatch.Restart();
        haveFrame = true;

        // Log the exact struct being sent — always once, right after the
        // first successful read (so there's something to compare against
        // HWiNFO/Task Manager immediately), then on the coarser cadence.
        bool dueToLog = logSensorMs > 0 &&
            (!loggedFirstFrame || logStopwatch.ElapsedMilliseconds >= logSensorMs);
        if (dueToLog)
        {
            FileLog.Write($"Sensors: {snapshot}");
            logStopwatch.Restart();
            loggedFirstFrame = true;
        }
    }

    if (!device.TryWrite(frame))
    {
        FileLog.Write("Write failed — panel disconnected, will retry.");
        continue; // device.Close() already called inside TryWrite on failure
    }

    try
    {
        await Task.Delay(intervalMs, cts.Token);
    }
    catch (TaskCanceledException)
    {
        break;
    }
}

FileLog.Write("JonsboMonitor stopping.");
if (singleInstanceMutex is not null)
{
    singleInstanceMutex.ReleaseMutex();
    singleInstanceMutex.Dispose();
}
return 0;
