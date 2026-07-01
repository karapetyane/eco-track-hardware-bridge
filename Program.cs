using PCSC;
using PCSC.Iso7816;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("EcoTrack Hardware Bridge");
Console.WriteLine("RFID UID reader mode");
Console.WriteLine();

using var context = ContextFactory.Instance.Establish(SCardScope.System);
var readers = context.GetReaders();

if (readers == null || readers.Length == 0)
{
    Console.WriteLine("No PC/SC readers found.");
    return;
}

var readerName = readers[0];

Console.WriteLine($"Using reader: {readerName}");
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
            Console.WriteLine($"Card UID: {uid}");
        }

        Thread.Sleep(2000);
    }
    catch
    {
        Thread.Sleep(500);
    }
}