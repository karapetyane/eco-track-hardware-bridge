using Fleck;
using System.Text.Json;
using EcoTrack.HardwareBridge.Models;

namespace EcoTrack.HardwareBridge.Services;

public sealed class WebSocketService
{
    private readonly WebSocketServer _server;
    private readonly List<IWebSocketConnection> _clients = new();

    public WebSocketService()
    {
        FleckLog.Level = LogLevel.Warn;

        _server = new WebSocketServer("ws://0.0.0.0:5001");

        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                Console.WriteLine("Frontend connected.");
                _clients.Add(socket);
            };

            socket.OnClose = () =>
            {
                Console.WriteLine("Frontend disconnected.");
                _clients.Remove(socket);
            };
        });
    }

    public void Broadcast(RfidEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);

        foreach (var client in _clients)
        {
            client.Send(json);
        }
    }
}