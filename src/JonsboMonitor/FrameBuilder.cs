namespace JonsboMonitor;

/// <summary>
/// Builds the 64-byte HID output report for the JONSBO panel (vid 20785 /
/// pid 8199, usagePage 0xFF00), reproducing `HIDClass.sendPic()`'s
/// default/generic 52-field branch from
/// main/_baseClass/device_hid.js byte-for-byte — see PROTOCOL.md for the
/// full field table and the rationale for each quirk reproduced here
/// (in particular the Fahrenheit "TempEnd" bug, which is kept faithfully
/// rather than fixed, since we don't know whether the firmware depends on
/// it).
/// </summary>
public static class FrameBuilder
{
    public const int FrameLength = 64;

    /// <summary>Display page selector: 0=CPU, 1=GPU, 2=RAM, 3=auto-cycle.</summary>
    public static int DisplayMode = 3;

    /// <summary>Seconds between auto-cycled pages when DisplayMode == 3.</summary>
    public static int CycleIntervalSeconds = 5;

    /// <summary>0 = show CPU on single-value pages, 1 = show GPU.</summary>
    public static int CpuFlag = 0;

    /// <summary>Vendor-specific display flag; vendor app's default is 1.</summary>
    public static int DarkFlash = 1;

    public static bool UseFahrenheit = false;

    public static byte[] Build(SensorSnapshot s, DateTime now)
    {
        var sendData = new int[52];

        int tempInt = UseFahrenheit ? 1 : 0;

        // --- CPU ---
        double cpuTempDisplay = ClampAndFloor(s.CpuTempC, isTemperature: true); // already-converted if Fahrenheit
        double cpuTempFrac = s.CpuTempC - cpuTempDisplay; // reproduces the vendor's mixed-unit bug verbatim
        int cpuTempStart = (int)ClampAndFloor(cpuTempDisplay, isTemperature: false);
        int cpuTempEnd = (int)ClampAndFloor(cpuTempFrac * 100, isTemperature: false);

        int cpuUnitInt = (int)ClampAndFloor(s.CpuUtilPct, isTemperature: false);
        double cpuUtilDecimal = ClampAndFloor((s.CpuUtilPct - cpuUnitInt) * 100, isTemperature: false);

        int cpuPower = (int)ClampAndFloor(s.CpuPowerW, isTemperature: false);
        int cpuPowerDrawStart = cpuPower % 100;
        int cpuPowerDrawEnd = (int)ClampAndFloor((s.CpuPowerW - cpuPower) * 100, isTemperature: false);
        int cpuPowerDrawExtra = cpuPower / 100;

        int cpuClock = (int)ClampAndFloor(s.CpuClockMHz, isTemperature: false);
        int cpuRefreshStart = cpuClock / 100;
        int cpuRefreshEnd = cpuClock % 100;

        int cpuVidInt = (int)ClampAndFloor(s.CpuVid, isTemperature: false);
        int cpuVidStart = cpuVidInt;
        int cpuVidEnd = (int)ClampAndFloor((s.CpuVid - cpuVidStart) * 100, isTemperature: false);

        int cpuFanRpm = (int)ClampAndFloor(s.CpuFanRpm, isTemperature: false);
        int cpuFanStart = cpuFanRpm / 100;
        int cpuFanEnd = cpuFanRpm % 100;

        // --- GPU ---
        double gpuTempDisplay = ClampAndFloor(s.GpuTempC, isTemperature: true);
        double gpuTempFrac = s.GpuTempC - gpuTempDisplay;
        int gpuTempStart = (int)ClampAndFloor(gpuTempDisplay, isTemperature: false);
        int gpuTempEnd = (int)ClampAndFloor(gpuTempFrac * 100, isTemperature: false);

        int gpuUnitInt = (int)ClampAndFloor(s.GpuUtilPct, isTemperature: false);
        double gpuUtilDecimal = ClampAndFloor((s.GpuUtilPct - gpuUnitInt) * 100, isTemperature: false);

        int gpuPower = (int)ClampAndFloor(s.GpuPowerW, isTemperature: false);
        int gpuPowerDrawStart = gpuPower % 100;
        int gpuPowerDrawEnd = (int)ClampAndFloor((s.GpuPowerW - gpuPower) * 100, isTemperature: false);
        int gpuPowerDrawExtra = gpuPower / 100;

        int gpuClock = (int)ClampAndFloor(s.GpuClockMHz, isTemperature: false);
        int gpuRefreshStart = gpuClock / 100;
        int gpuRefreshEnd = gpuClock % 100;

        // --- fans / mem ---
        int caseFanRpm = (int)ClampAndFloor(s.CaseFanRpm, isTemperature: false);
        int caseFanStart = caseFanRpm / 100;
        int caseFanEnd = caseFanRpm % 100;

        int memUsage = (int)ClampAndFloor(s.RamUsedPct, isTemperature: false);
        double memUtilDecimal = ClampAndFloor((s.RamUsedPct - memUsage) * 100, isTemperature: false);

        int ramClock = (int)ClampAndFloor(s.RamClockMHz, isTemperature: false);
        int ramClockStart = ramClock / 100;
        int ramClockEnd = ramClock % 100;

        // --- date (matches getFormattedDate exactly) ---
        int yearStar = int.Parse(now.Year.ToString().Substring(0, 2));
        int yearEnd = int.Parse(now.Year.ToString().Substring(2));
        int dayOfWeek = (int)now.DayOfWeek; // .NET: Sunday=0 .. Saturday=6, same convention as JS getDay()

        sendData[0] = 1;
        sendData[1] = 0;
        sendData[2] = 1;
        sendData[3] = 2;
        sendData[4] = cpuTempStart;
        sendData[5] = cpuTempEnd;
        sendData[6] = tempInt;
        sendData[7] = cpuUnitInt;
        sendData[8] = cpuPowerDrawStart;
        sendData[9] = cpuPowerDrawEnd;
        sendData[10] = cpuRefreshStart;
        sendData[11] = cpuRefreshEnd;
        sendData[12] = cpuVidStart;
        sendData[13] = cpuVidEnd;
        sendData[14] = gpuTempStart;
        sendData[15] = gpuTempEnd;
        sendData[16] = tempInt;
        sendData[17] = gpuUnitInt;
        sendData[18] = gpuPowerDrawStart;
        sendData[19] = gpuPowerDrawEnd;
        sendData[20] = gpuRefreshStart;
        sendData[21] = gpuRefreshEnd;
        sendData[22] = cpuFanStart;
        sendData[23] = cpuFanEnd;
        sendData[24] = caseFanStart;
        sendData[25] = caseFanEnd;
        sendData[26] = yearStar;
        sendData[27] = yearEnd;
        sendData[28] = now.Month;
        sendData[29] = now.Day;
        sendData[30] = now.Hour;
        sendData[31] = now.Minute;
        sendData[32] = now.Second;
        sendData[33] = dayOfWeek;
        sendData[34] = memUsage;
        sendData[35] = cpuPowerDrawExtra;
        sendData[36] = gpuPowerDrawExtra;
        sendData[37] = DisplayMode;
        sendData[38] = CycleIntervalSeconds;
        sendData[39] = CpuFlag;
        sendData[40] = DarkFlash;
        sendData[41] = DisplayMode;
        sendData[42] = DisplayMode;
        sendData[43] = 0; // mem_TempStart — RAM temp sensors are rarely available, matches vendor default
        sendData[44] = 0; // mem_TempEnd
        sendData[45] = tempInt;
        sendData[46] = ramClockStart;
        sendData[47] = ramClockEnd;
        sendData[48] = DisplayMode;
        sendData[49] = (int)cpuUtilDecimal;
        sendData[50] = (int)gpuUtilDecimal;
        sendData[51] = (int)memUtilDecimal;

        var frame = new byte[FrameLength];
        for (int i = 0; i < sendData.Length; i++)
            frame[i] = (byte)Math.Clamp(sendData[i], 0, 255);
        // indices 52..63 stay 0 (padding), matching formatSendData's zero-fill
        return frame;
    }

    /// <summary>Shared clamp-and-floor step the vendor protocol applies to
    /// every numeric field before it goes in the frame (see PROTOCOL.md):
    /// clamp to [0, max] then floor. Temperature fields clamp to 999 and
    /// apply the Celsius→Fahrenheit conversion before flooring; everything
    /// else clamps to 9999.</summary>
    private static double ClampAndFloor(double num, bool isTemperature)
    {
        double max = isTemperature ? 999 : 9999;
        double numVal = Math.Min(double.IsNaN(num) ? 0 : num, max);
        if (numVal < 0) numVal = 0;
        return isTemperature ? Math.Floor(CelsiusToFahrenheit(numVal)) : Math.Floor(numVal);
    }

    private static double CelsiusToFahrenheit(double celsius) =>
        UseFahrenheit ? (9.0 / 5.0) * celsius + 32 : celsius;
}
