using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EcoTrack.HardwareBridge.Services;

public sealed class FileLogger
{
    public const string LogDirectory = @"C:\ProgramData\EcoTrack\Logs";
    public const string LogFileName = "hardware-bridge.log";
    public static readonly string LogFilePath = Path.Combine(LogDirectory, LogFileName);

    public static FileLogger Instance { get; } = new();

    private readonly object _writeLock = new();
    private bool _initialized;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    public void EnsureInitialized()
    {
        lock (_writeLock)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogDirectory);

                if (!File.Exists(LogFilePath))
                {
                    using (File.Create(LogFilePath))
                    {
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                ReportWriteFailure("Failed to initialize log file", ex);
            }
        }
    }

    public void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        EnsureInitialized();

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message.Trim()}";

        lock (_writeLock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                using var stream = new FileStream(
                    LogFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine(line);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            catch (Exception ex)
            {
                ReportWriteFailure($"Failed to write log line: {line}", ex);
            }
        }
    }

    public void LogException(string context, Exception exception)
    {
        Log($"{context}: {exception.Message}");
    }

    private static void ReportWriteFailure(string context, Exception exception)
    {
        var message = $"[EcoTrack.HardwareBridge] {context}: {exception}";

        Trace.WriteLine(message);
        Debug.WriteLine(message);

        if (GetConsoleWindow() == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Console may be unavailable even when a window handle exists.
        }
    }
}
