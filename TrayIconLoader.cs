using System.Drawing;
using System.Reflection;

namespace EcoTrack.HardwareBridge;

internal static class TrayIconLoader
{
    public static Icon Load()
    {
        var fromAssets = TryLoadFromAssets();
        if (fromAssets != null)
        {
            return fromAssets;
        }

        var fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (fromExe != null)
        {
            return fromExe;
        }

        return SystemIcons.Application;
    }

    private static Icon? TryLoadFromAssets()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico"),
            Path.Combine(AppContext.BaseDirectory, "tray.ico"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return new Icon(path);
            }
            catch
            {
                // Try the next candidate.
            }
        }

        var resourceName = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("tray.ico", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            return null;
        }

        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            return stream == null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}
