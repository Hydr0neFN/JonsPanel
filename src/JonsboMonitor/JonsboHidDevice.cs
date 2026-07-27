using HidSharp;

namespace JonsboMonitor;

/// <summary>
/// Auto-detects and talks to the JONSBO panel over raw USB HID
/// (VID 0x5131 / PID 0x2007) instead of a hardcoded COM port — the device
/// is USB HID, not the CH340 serial adapter (see PROTOCOL.md's headline
/// finding). Never fuzzes report IDs or writes anything other than the
/// frame the caller supplies.
/// </summary>
public sealed class JonsboHidDevice : IDisposable
{
    public const int VendorId = 0x5131;  // 20785
    public const int ProductId = 0x2007; // 8199

    private HidStream? _stream;

    /// <summary>True if currently holding an open handle to the device.</summary>
    public bool IsOpen => _stream is not null;

    /// <summary>
    /// Attempts to find and open the panel. Returns false (never throws) if
    /// no matching device is currently plugged in — callers should log and
    /// retry after a delay rather than spin.
    /// </summary>
    public bool TryOpen()
    {
        if (_stream is not null) return true;

        List<HidDevice> matches;
        try
        {
            matches = DeviceList.Local.GetHidDevices(VendorId, ProductId).ToList();
        }
        catch (Exception)
        {
            return false; // enumeration can transiently fail during device (re)plug events
        }

        if (matches.Count == 0) return false;

        // The confirmed-working interface has an output report length of
        // 65 bytes on the wire (1 report-ID byte + 64 payload bytes,
        // matching the vendor's 64-byte sendList whose first byte doubles
        // as the report ID) — prefer that interface if there's more than
        // one, otherwise fall back to whichever matched.
        HidDevice device =
            matches.FirstOrDefault(d => SafeMaxOutputReportLength(d) == 65)
            ?? matches[0];

        try
        {
            if (device.TryOpen(out HidStream? stream) && stream is not null)
            {
                _stream = stream;
                return true;
            }
        }
        catch (Exception)
        {
            // Access denied / device busy / unplugged mid-open — treat as
            // "not available right now", caller retries later.
        }
        return false;
    }

    /// <summary>
    /// Writes the given 64-byte frame as-is (fire-and-forget, no ack — this
    /// matches the vendor app, which never reads a response back from this
    /// device). Returns false if the write failed, meaning the device has
    /// likely been unplugged; caller should close and retry.
    /// </summary>
    public bool TryWrite(byte[] frame)
    {
        if (_stream is null) return false;
        try
        {
            _stream.Write(frame);
            return true;
        }
        catch (Exception)
        {
            Close();
            return false;
        }
    }

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Close();

    private static int SafeMaxOutputReportLength(HidDevice d)
    {
        try { return d.GetMaxOutputReportLength(); }
        catch { return -1; }
    }
}
