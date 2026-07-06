using System.Text.Json;
using EcoTrack.HardwareBridge.Services;

namespace EcoTrack.HardwareBridge.Configuration;

public static class BridgeConfigLoader
{
    public const string ConfigFileName = "bridge.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static BridgeSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);

        if (!File.Exists(path))
        {
            return BridgeSettings.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<BridgeSettings>(json, JsonOptions)
                ?? BridgeSettings.CreateDefault();

            return Normalize(settings);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.LogException("Failed to load bridge.config.json", ex);
            return BridgeSettings.CreateDefault();
        }
    }

    private static BridgeSettings Normalize(BridgeSettings settings)
    {
        settings.ApiUrl = settings.ApiUrl?.Trim() ?? "";
        settings.ApiKey = settings.ApiKey?.Trim() ?? "";
        settings.BridgeId = settings.BridgeId?.Trim() ?? "";
        settings.WebSocket ??= WebSocketSettings.CreateDefault();
        settings.WebSocket.Host = string.IsNullOrWhiteSpace(settings.WebSocket.Host)
            ? "0.0.0.0"
            : settings.WebSocket.Host.Trim();

        if (settings.WebSocket.Port <= 0)
        {
            settings.WebSocket.Port = 5001;
        }

        return settings;
    }
}
