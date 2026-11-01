using System.Text.Json;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Export.Mono.Printers;
using ReceiptPrinting.Profiles;

namespace ReceiptPrinting.Printing;

/// <summary>The commands for one page, and whether the job should pause after it.</summary>
public readonly record struct CommandSegment(ReadOnlyMemory<byte> Bytes, bool Pace);

/// <summary>
/// The ESC/POS commands of a job, one page at a time. The bytes come from
/// BluePrint's <see cref="PrinterCommands.Encode"/> only - this application never
/// writes printer commands of its own.
/// <para>The pages one after another must give exactly the bytes that encoding
/// the whole job at once gives. What ESC/POS sends once per job is switched on
/// for one page only: ESC @ (Initialize) before the first, the cut at the end and
/// the cash drawer after the last.</para>
/// </summary>
public sealed class CommandSource(ReceiptCommands commands)
{
    private readonly string _json = JsonSerializer.Serialize(commands.ToOptions());

    /// <param name="index">Position in the job, from 0.</param>
    /// <param name="count">Pages in the job - the last one has to be known before it is sent.</param>
    public CommandSegment Page(MonochromeBitmap page, int index, int count)
    {
        bool first = index == 0, last = index == count - 1;
        var options = JsonSerializer.Deserialize<PrinterCommandOptions>(_json)!;
        options.Initialize &= first;
        options.EscPos.CutAtEnd &= last;
        options.EscPos.OpenDrawer &= last;
        return new CommandSegment(PrinterCommands.Encode([page], PrinterLanguage.EscPos, options), Pace: !last);
    }
}
