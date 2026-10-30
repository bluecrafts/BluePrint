using System.Text.Json;
using BluePrint.Render.Export.Mono;
using BluePrint.Render.Export.Mono.Printers;

namespace LabelPrinting.Printing;

/// <summary>The commands for one row of labels, and whether the job should pause after it.</summary>
public readonly record struct CommandSegment(ReadOnlyMemory<byte> Bytes, bool Pace);

/// <summary>
/// The printer commands of a job, one row of labels at a time. The bytes come
/// from BluePrint's <see cref="PrinterCommands.Encode"/> only - this application
/// never writes printer commands of its own.
/// <para>The rows one after another must give exactly the bytes that encoding
/// the whole job at once gives. What the label languages send once per job -
/// the label size, gap, direction, darkness and speed - comes with Initialize,
/// so it is switched on for the first row only.</para>
/// </summary>
public sealed class CommandSource(PrinterLanguage language, PrinterCommandOptions options)
{
    /// <param name="index">Position in the job, from 0.</param>
    /// <param name="count">Rows in the job - the last one has to be known before it is sent.</param>
    /// <param name="allowInitialize">false for a reprint: the printer was set up by the first row already.</param>
    public CommandSegment Page(MonochromeBitmap page, int index, int count, bool allowInitialize = true)
    {
        var rowOptions = JsonSerializer.Deserialize<PrinterCommandOptions>(JsonSerializer.Serialize(options))!;
        rowOptions.Initialize = options.Initialize && index == 0 && allowInitialize;
        return new CommandSegment(PrinterCommands.Encode([page], language, rowOptions), Pace: index != count - 1);
    }
}
