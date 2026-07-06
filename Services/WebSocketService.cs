using Fleck;
using System.Text.Json;
using EcoTrack.HardwareBridge.Configuration;
using EcoTrack.HardwareBridge.Models;

namespace EcoTrack.HardwareBridge.Services;

public sealed class WebSocketService : IDisposable
{
    private readonly WebSocketServer _server;
    private readonly List<IWebSocketConnection> _clients = new();
    private readonly object _clientsLock = new();

    public WebSocketService(WebSocketSettings settings, Action<string>? consoleMirror = null)
    {
        FleckLog.Level = LogLevel.Warn;

        _server = new WebSocketServer($"ws://{settings.Host}:{settings.Port}");

        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                FileLogger.Instance.Log("Frontend connected");
                consoleMirror?.Invoke("Frontend connected");
                lock (_clientsLock)
                {
                    _clients.Add(socket);
                }
            };

            socket.OnClose = () =>
            {
                FileLogger.Instance.Log("Frontend disconnected");
                consoleMirror?.Invoke("Frontend disconnected");
                lock (_clientsLock)
                {
                    _clients.Remove(socket);
                }
            };
        });
    }

    public int ConnectedClientCount
    {
        get
        {
            lock (_clientsLock)
            {
                return _clients.Count;
            }
        }
    }

    public void Broadcast(RfidEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        IWebSocketConnection[] clients;

        lock (_clientsLock)
        {
            clients = _clients.ToArray();
        }

        foreach (var client in clients)
        {
            client.Send(json);
        }
    }

    public void Dispose()
    {
        IWebSocketConnection[] clients;

        lock (_clientsLock)
        {
            clients = _clients.ToArray();
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.LogException("WebSocket client close error", ex);
            }
        }

        _server.Dispose();
    }
}
