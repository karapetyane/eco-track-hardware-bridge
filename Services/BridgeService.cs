using PCSC;
using PCSC.Exceptions;
using PCSC.Iso7816;
using EcoTrack.HardwareBridge.Models;

namespace EcoTrack.HardwareBridge.Services;

public sealed class BridgeService : IDisposable
{
    private const int PollIntervalMs = 300;
    private const int MaxConsecutiveReaderFailures = 5;
    private static readonly TimeSpan RecoveryBackoff = TimeSpan.FromSeconds(2);

    // PC/SC errors that simply mean "no readable card right now" — these are normal
    // during idle polling and must NOT trigger reader recovery.
    private static readonly HashSet<SCardError> CardAbsentErrors = new()
    {
        SCardError.RemovedCard,
        SCardError.NoSmartcard,
        SCardError.UnpoweredCard,
        SCardError.UnresponsiveCard,
        SCardError.ResetCard,
        SCardError.ProtocolMismatch,
        SCardError.NotReady,
        SCardError.SharingViolation,
        SCardError.Timeout,
    };

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
        LogRuntimeEvent("WebSocket listening on ws://localhost:5001");

        var recovering = false;
        var recoveryAnnounced = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            ISCardContext? context = null;
            string? readerName = null;

            try
            {
                context = ContextFactory.Instance.Establish(SCardScope.System);
                var readers = context.GetReaders();
                readerName = readers is { Length: > 0 } ? readers[0] : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FileLogger.Instance.LogException("RFID polling error", ex);
                MirrorToConsole($"RFID polling error: {ex.Message}");
                readerName = null;
            }

            // No reader available: either none is attached yet, or a recovery attempt failed.
            if (readerName == null)
            {
                DisposeContext(context);

                if (recovering && recoveryAnnounced)
                {
                    LogRuntimeEvent("Reader recovery failed");
                }
                else if (!recovering)
                {
                    LogRuntimeEvent("Reader missing: No PC/SC readers found.");
                    SetStatus(BridgeStatus.NoReader, "No PC/SC readers found.");
                }

                recovering = true;
                if (!recoveryAnnounced)
                {
                    LogRuntimeEvent("Reader recovery started");
                    recoveryAnnounced = true;
                }

                if (!await DelaySafeAsync(RecoveryBackoff, cancellationToken))
                {
                    break;
                }

                continue;
            }

            // Reader available.
            ReaderName = readerName;

            if (recovering)
            {
                LogRuntimeEvent("Reader recovery completed");
                recovering = false;
                recoveryAnnounced = false;
            }

            LogRuntimeEvent($"Reader selected: {readerName}");
            SetStatus(BridgeStatus.Running, $"Running on {readerName}");

            try
            {
                await PollReaderAsync(context!, readerName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FileLogger.Instance.LogException("RFID polling error", ex);
                MirrorToConsole($"RFID polling error: {ex.Message}");
            }
            finally
            {
                DisposeContext(context);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // PollReaderAsync returned because the reader stopped responding: begin recovery.
            recovering = true;
            recoveryAnnounced = true;
            LogRuntimeEvent("Reader recovery started");
            SetStatus(BridgeStatus.Error, "Reader not responding; recovering...");

            if (!await DelaySafeAsync(RecoveryBackoff, cancellationToken))
            {
                break;
            }
        }

        if (!cancellationToken.IsCancellationRequested && Status is BridgeStatus.Running or BridgeStatus.Starting)
        {
            SetStatus(BridgeStatus.Stopped, "Stopped");
        }
    }

    // Polls a single reader/context until cancellation or a fatal reader failure (returns).
    private async Task PollReaderAsync(ISCardContext context, string readerName, CancellationToken cancellationToken)
    {
        string? lastUid = null;
        var cardPresent = false;
        var consecutiveFailures = 0;

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

                consecutiveFailures = 0;

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
                else if (cardPresent)
                {
                    HandleCardRemoved(ref cardPresent, ref lastUid);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (IsCardAbsentError(ex))
                {
                    // Normal idle state: no card on the reader.
                    consecutiveFailures = 0;
                    if (cardPresent)
                    {
                        HandleCardRemoved(ref cardPresent, ref lastUid);
                    }
                }
                else
                {
                    // Genuine reader/context failure.
                    consecutiveFailures++;
                    FileLogger.Instance.LogException("RFID polling error", ex);
                    MirrorToConsole($"RFID polling error: {ex.Message}");

                    if (cardPresent)
                    {
                        HandleCardRemoved(ref cardPresent, ref lastUid);
                    }

                    if (consecutiveFailures >= MaxConsecutiveReaderFailures)
                    {
                        // Escalate to the outer loop to recreate the PC/SC context.
                        return;
                    }
                }
            }

            if (!await DelaySafeAsync(TimeSpan.FromMilliseconds(PollIntervalMs), cancellationToken))
            {
                return;
            }
        }
    }

    private void HandleCardRemoved(ref bool cardPresent, ref string? lastUid)
    {
        FileLogger.Instance.Log("Card removed");
        MirrorToConsole("Card removed");
        cardPresent = false;
        lastUid = null;
        LastUid = null;
        CardPresent = false;
        NotifyStatusChanged();
    }

    private static bool IsCardAbsentError(Exception ex)
    {
        return ex is PCSCException pcsc && CardAbsentErrors.Contains(pcsc.SCardError);
    }

    private static void DisposeContext(ISCardContext? context)
    {
        try
        {
            context?.Dispose();
        }
        catch
        {
            // Best-effort cleanup during reader recovery.
        }
    }

    private static async Task<bool> DelaySafeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
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
