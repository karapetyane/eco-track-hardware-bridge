using System.Runtime.InteropServices;
using EcoTrack.HardwareBridge.Services;

namespace EcoTrack.HardwareBridge;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [STAThread]
    private static void Main(string[] args)
    {
        var consoleMode = ShouldRunInConsoleMode(args);

        if (consoleMode)
        {
            AllocConsole();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            RunConsoleMode();
            return;
        }

        ApplicationConfiguration.Initialize();
        var bridge = new BridgeService();
        Application.Run(new TrayApplicationContext(bridge));
    }

    private static bool ShouldRunInConsoleMode(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var env = Environment.GetEnvironmentVariable("ECOTRACK_BRIDGE_CONSOLE");
        return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunConsoleMode()
    {
        Console.WriteLine("EcoTrack Hardware Bridge");
        Console.WriteLine("RFID monitor + WebSocket mode (console)");
        Console.WriteLine();

        using var bridge = new BridgeService(Console.WriteLine);
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
