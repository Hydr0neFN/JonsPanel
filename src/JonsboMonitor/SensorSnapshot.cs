namespace JonsboMonitor;

/// <summary>
/// Plain snapshot of the sensor values the JONSBO panel's default/generic
/// 52-field frame needs, per PROTOCOL.md. All values are "0 if unavailable"
/// rather than null, mirroring the vendor app's own getIntValue(num || 0)
/// pattern — a missing sensor just shows 0 on the panel instead of crashing
/// the write loop.
/// </summary>
public sealed class SensorSnapshot
{
    public double CpuTempC { get; init; }
    public double CpuUtilPct { get; init; }
    public double CpuPowerW { get; init; }
    public double CpuClockMHz { get; init; }
    public double CpuVid { get; init; }
    public double CpuFanRpm { get; init; }

    public double GpuTempC { get; init; }
    public double GpuUtilPct { get; init; }
    public double GpuPowerW { get; init; }
    public double GpuClockMHz { get; init; }

    /// <summary>Secondary/case fan (fanLayout_fanSpeed in the vendor app).</summary>
    public double CaseFanRpm { get; init; }

    public double RamUsedPct { get; init; }
    public double RamClockMHz { get; init; }
    public double RamTotalGB { get; init; }

    public static SensorSnapshot Empty { get; } = new();

    /// <summary>
    /// Compact one-line dump of every field, in the same order the panel
    /// displays them. Exists so the exact struct being sent can be logged
    /// and eyeballed against HWiNFO/Task Manager per-field, instead of only
    /// confirming the panel "lit up" — see README "Known rough edges".
    /// </summary>
    public override string ToString() =>
        $"CPU[temp={CpuTempC:F1}C util={CpuUtilPct:F0}% power={CpuPowerW:F1}W " +
        $"clock={CpuClockMHz:F0}MHz vid={CpuVid:F3}V fan={CpuFanRpm:F0}rpm] " +
        $"GPU[temp={GpuTempC:F1}C util={GpuUtilPct:F0}% power={GpuPowerW:F1}W clock={GpuClockMHz:F0}MHz] " +
        $"CaseFan={CaseFanRpm:F0}rpm " +
        $"RAM[used={RamUsedPct:F0}% clock={RamClockMHz:F0}MHz total={RamTotalGB:F1}GB]";
}
