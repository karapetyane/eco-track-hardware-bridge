using PCSC;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("EcoTrack Hardware Bridge");
Console.WriteLine("Searching for PC/SC readers...");
Console.WriteLine();

using var context = ContextFactory.Instance.Establish(SCardScope.System);
var readers = context.GetReaders();

if (readers == null || readers.Length == 0)
{
    Console.WriteLine("No PC/SC readers found.");
    return;
}

Console.WriteLine($"Found {readers.Length} reader(s):");

foreach (var reader in readers)
{
    Console.WriteLine($"- {reader}");
}

Console.WriteLine();
Console.WriteLine("Done.");