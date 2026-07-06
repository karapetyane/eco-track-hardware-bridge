using System.Text.Json.Serialization;

namespace EcoTrack.HardwareBridge.Models;

public sealed class CloudRfidEventRequest
{
    [JsonPropertyName("event")]
    public string Event { get; init; } = "rfid";

    [JsonPropertyName("uid")]
    public string Uid { get; init; } = "";

    [JsonPropertyName("readAt")]
    public DateTime ReadAt { get; init; }

    [JsonPropertyName("bridgeId")]
    public string BridgeId { get; init; } = "";

    [JsonPropertyName("checkpointId")]
    public int? CheckpointId { get; init; }
}
