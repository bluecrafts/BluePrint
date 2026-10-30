using System.Printing;

namespace LabelPrinting.Transports.Discovery;

/// <summary>The Windows print queues, local and connected, shown with their port: "Label printer (USB001)".</summary>
public static class SpoolerQueueDiscovery
{
    public static IReadOnlyList<DiscoveredDevice> Find()
    {
        using var server = new LocalPrintServer();
        using var queues = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]);

        var result = new List<DiscoveredDevice>();
        foreach (var queue in queues)
        {
            using (queue)
            {
                string? port = null;
                try { port = queue.QueuePort?.Name; }
                catch (PrintSystemException) { /* a connected queue may not tell its port: show the name alone */ }
                result.Add(new DiscoveredDevice(queue.FullName, queue.FullName, port));
            }
        }
        return [.. result.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }
}
