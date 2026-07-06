namespace EcoTrack.HardwareBridge.Configuration;

public sealed class BridgeSettings
{
    public string ApiUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public string BridgeId { get; set; } = "";

    public int? CheckpointId { get; set; }

    public bool EnableCloudSync { get; set; }

    public WebSocketSettings WebSocket { get; set; } = WebSocketSettings.CreateDefault();

    public static BridgeSettings CreateDefault() => new();
}

public sealed class WebSocketSettings
{
    public string Host { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 5001;

    public static WebSocketSettings CreateDefault() => new();
}
