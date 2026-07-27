namespace JonsboMonitor;

/// <summary>
/// Minimal append-only file logger, no external dependency. Writes next to
/// the exe (or wherever the scheduled task's working directory is) so a
/// running-as-a-task instance leaves a trail without an attached console.
/// Truncates itself once it passes ~5MB instead of growing forever.
/// </summary>
public static class FileLog
{
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogPath = Path.Combine(LogDir, "jonsbo-monitor.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";

        // Mirror to console too, for interactive/manual runs.
        Console.WriteLine(line);

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the send loop.
        }
    }
}
