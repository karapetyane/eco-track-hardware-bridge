using System.Runtime.InteropServices;
using System.Threading;
using EcoTrack.HardwareBridge.Configuration;
using EcoTrack.HardwareBridge.Services;

namespace EcoTrack.HardwareBridge;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\EcoTrack.HardwareBridge.SingleInstance";

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [STAThread]
    private static void Main(string[] args)
    {
        FileLogger.Instance.EnsureInitialized();

        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isPrimaryInstance);

        if (!isPrimaryInstance)
        {
            FileLogger.Instance.Log("Duplicate instance detected; exiting.");
            return;
        }

        try
        {
            var settings = BridgeConfigLoader.Load();
            LogStartupConfiguration(settings);

            var consoleMode = ShouldRunInConsoleMode(args);

            if (consoleMode)
            {
                AllocConsole();
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                RunConsoleMode(settings);
                return;
            }

            ApplicationConfiguration.Initialize();
            var bridge = new BridgeService(settings);
            Application.Run(new TrayApplicationContext(bridge));
        }
        finally
        {
            singleInstanceMutex.ReleaseMutex();
        }
    }

    private static void LogStartupConfiguration(BridgeSettings settings)
    {
        var bridgeId = string.IsNullOrWhiteSpace(settings.BridgeId) ? "(not set)" : settings.BridgeId.Trim();
        var checkpointId = settings.CheckpointId?.ToString() ?? "(not set)";
        var apiUrl = string.IsNullOrWhiteSpace(settings.ApiUrl) ? "(not set)" : settings.ApiUrl.Trim();
        var webSocketUrl = $"ws://{settings.WebSocket.Host}:{settings.WebSocket.Port}";

        FileLogger.Instance.Log($"BridgeId: {bridgeId}");
        FileLogger.Instance.Log($"CheckpointId: {checkpointId}");
        FileLogger.Instance.Log($"ApiUrl: {apiUrl}");
        FileLogger.Instance.Log($"EnableCloudSync: {settings.EnableCloudSync}");
        FileLogger.Instance.Log($"WebSocket: {webSocketUrl}");
    }

    private static bool ShouldRunInConsoleMode(string[] args)
    {
        return args.Any(arg => string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase));
    }

    private static void RunConsoleMode(BridgeSettings settings)
    {
        Console.WriteLine("EcoTrack Hardware Bridge");
        Console.WriteLine("RFID monitor + WebSocket mode (console)");
        Console.WriteLine();

        using var bridge = new BridgeService(settings, mirrorConsole: true);
        using var shutdown = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Set();
        };

        bridge.Start();

        shutdown.Wait();
        bridge.Stop();

        Console.WriteLine();
        Console.WriteLine("Bridge stopped.");
    }
}
