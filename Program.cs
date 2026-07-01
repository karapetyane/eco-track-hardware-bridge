using PCSC;
using PCSC.Iso7816;
using EcoTrack.HardwareBridge.Models;
using EcoTrack.HardwareBridge.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("EcoTrack Hardware Bridge");
Console.WriteLine("RFID monitor + WebSocket mode");
Console.WriteLine();

var webSocketService = new WebSocketService();

using var context = ContextFactory.Instance.Establish(SCardScope.System);
var readers = context.GetReaders();

if (readers == null || readers.Length == 0)
{
    Console.WriteLine("No PC/SC readers found.");
    return;
}

var readerName = readers[0];
string? lastUid = null;
var cardPresent = false;

Console.WriteLine($"Using reader: {readerName}");
Console.WriteLine("WebSocket listening on ws://localhost:5001");
Console.WriteLine("Place RFID card on reader...");
Console.WriteLine();

while (true)
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
                Console.WriteLine($"Card detected: {uid}");

                webSocketService.Broadcast(new RfidEvent
                {
                    Uid = uid,
                    ReadAt = DateTime.UtcNow
                });

                lastUid = uid;
                cardPresent = true;
            }
        }
    }
    catch
    {
        if (cardPresent)
        {
            Console.WriteLine("Card removed");
            cardPresent = false;
            lastUid = null;
        }
    }

    Thread.Sleep(300);
}