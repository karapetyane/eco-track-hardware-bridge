using System.Text;
using System.Text.Json;
using EcoTrack.HardwareBridge.Configuration;
using EcoTrack.HardwareBridge.Models;

namespace EcoTrack.HardwareBridge.Services;

public sealed class CloudSyncService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly BridgeSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly string? _endpointUrl;

    public CloudSyncService(BridgeSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        if (_settings.EnableCloudSync && !string.IsNullOrWhiteSpace(_settings.ApiUrl))
        {
            _endpointUrl = $"{_settings.ApiUrl.TrimEnd('/')}/api/v1/hardware/rfid/scans";
        }
    }

    public void TryPublishRfidScan(RfidEvent evt)
    {
        if (!_settings.EnableCloudSync)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_endpointUrl))
        {
            FileLogger.Instance.Log("Cloud RFID event skipped: ApiUrl is not configured");
            return;
        }

        var request = new CloudRfidEventRequest
        {
            Event = evt.Event,
            Uid = evt.Uid,
            ReadAt = evt.ReadAt,
            BridgeId = _settings.BridgeId,
            CheckpointId = _settings.CheckpointId,
        };

        _ = Task.Run(() => PublishAsync(request));
    }

    private async Task PublishAsync(CloudRfidEventRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                httpRequest.Headers.Add("X-Bridge-Api-Key", _settings.ApiKey);
            }

            using var response = await _httpClient.SendAsync(httpRequest).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                FileLogger.Instance.Log($"Cloud RFID event sent: {request.Uid}");
                return;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body)
                ? $"HTTP {(int)response.StatusCode}"
                : $"HTTP {(int)response.StatusCode}: {body.Trim()}";

            FileLogger.Instance.Log($"Cloud RFID event failed: {request.Uid}: {detail}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Log($"Cloud RFID event failed: {request.Uid}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
