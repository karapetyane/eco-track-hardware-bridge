using PCSC;
using PCSC.Iso7816;
using EcoTrack.HardwareBridge.Models;

namespace EcoTrack.HardwareBridge.Services;

public sealed class BridgeService : IDisposable
{
    private readonly WebSocketService _webSocket;
    private readonly bool _mirrorConsole;
    private readonly object _statusLock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public BridgeService(bool mirrorConsole = false)
    {
        _mirrorConsole = mirrorConsole;
        _webSocket = new WebSocketService(mirrorConsole ? Console.WriteLine : null);
    }

    public BridgeStatus Status { get; private set; } = BridgeStatus.Stopped;

    public string StatusMessage { get; private set; } = "Stopped";

    public string? ReaderName { get; private set; }

    public string? LastUid { get; private set; }

    public bool CardPresent { get; private set; }

    public int ConnectedClients => _webSocket.ConnectedClientCount;

    public event Action? StatusChanged;

    public void Start()
    {
        if (_loopTask is { IsCompleted: false })
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        LogRuntimeEvent("Bridge started");
        SetStatus(BridgeStatus.Starting, "Starting RFID monitor and WebSocket...");

        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.InnerExceptions)
            {
                if (inner is not OperationCanceledException)
                {
                    FileLogger.Instance.LogException("Bridge stop error", inner);
                }
            }
        }

        LogRuntimeEvent("Bridge stopped");
        SetStatus(BridgeStatus.Stopped, "Stopped");

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public string GetStatusSummary()
    {
        lock (_statusLock)
        {
            var lines = new List<string>
            {
                "EcoTrack Hardware Bridge",
                "",
                $"Status: {Status}",
                $"Detail: {StatusMessage}",
                $"Reader: {ReaderName ?? "—"}",
                $"WebSocket: ws://localhost:5001",
                $"Connected clients: {ConnectedClients}",
                $"Card present: {(CardPresent ? "Yes" : "No")}",
            };

            if (!string.IsNullOrWhiteSpace(LastUid))
            {
                lines.Add($"Last UID: {LastUid}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _webSocket.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var context = ContextFactory.Instance.Establish(SCardScope.System);
            var readers = context.GetReaders();

            if (readers == null || readers.Length == 0)
            {
                LogRuntimeEvent("Reader missing: No PC/SC readers found.");
                SetStatus(BridgeStatus.NoReader, "No PC/SC readers found.");
                return;
            }

            var readerName = readers[0];
            ReaderName = readerName;
            string? lastUid = null;
            var cardPresent = false;

            LogRuntimeEvent($"Reader selected: {readerName}");
            LogRuntimeEvent("WebSocket listening on ws://localhost:5001");

            SetStatus(BridgeStatus.Running, $"Running on {readerName}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var isoReader = new IsoReader(
                        context,
                        readerName,
                        SCardShareMode.Shared,
                        SCardProtocol.Any,
                        false
                    );

                    var apdu = new CommandApdu(IsoCase.Case2Short, isoReader.ActiveProtocol)
                    {
                        CLA = 0xFF,
                        INS = 0xCA,
                        P1 = 0x00,
                        P2 = 0x00,
                        Le = 0x00
                    };

                    var response = isoReader.Transmit(apdu);

                    if (response.SW1 == 0x90 && response.SW2 == 0x00)
                    {
                        var uid = BitConverter.ToString(response.GetData()).Replace("-", "");

                        if (!cardPresent || uid != lastUid)
                        {
                            FileLogger.Instance.Log($"Card detected: {uid}");
                            MirrorToConsole($"Card detected: {uid}");

                            _webSocket.Broadcast(new RfidEvent
                            {
                                Uid = uid,
                                ReadAt = DateTime.UtcNow
                            });

                            lastUid = uid;
                            cardPresent = true;
                            LastUid = uid;
                            CardPresent = true;
                            NotifyStatusChanged();
                        }
                    }
                }
                catch
                {
                    if (cardPresent)
                    {
                        FileLogger.Instance.Log("Card removed");
                        MirrorToConsole("Card removed");
                        cardPresent = false;
                        lastUid = null;
                        LastUid = null;
                        CardPresent = false;
                        NotifyStatusChanged();
                    }
                }

                try
                {
                    await Task.Delay(300, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FileLogger.Instance.LogException("Bridge error", ex);
            SetStatus(BridgeStatus.Error, ex.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && Status is BridgeStatus.Running or BridgeStatus.Starting)
            {
                SetStatus(BridgeStatus.Stopped, "Stopped");
            }
        }
    }

    private void LogRuntimeEvent(string message)
    {
        FileLogger.Instance.Log(message);
        MirrorToConsole(message);
    }

    private void MirrorToConsole(string message)
    {
        if (_mirrorConsole)
        {
            Console.WriteLine(message);
        }
    }

    private void SetStatus(BridgeStatus status, string message)
    {
        lock (_statusLock)
        {
            Status = status;
            StatusMessage = message;
        }

        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        StatusChanged?.Invoke();
    }
}
