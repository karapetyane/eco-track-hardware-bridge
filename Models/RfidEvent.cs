namespace EcoTrack.HardwareBridge.Models;

public sealed class RfidEvent
{
    public string Event { get; init; } = "rfid";
    public string Uid { get; init; } = "";
    public DateTime ReadAt { get; init; } = DateTime.UtcNow;
}