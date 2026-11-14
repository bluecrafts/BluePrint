// Renders a report once for a set of conditions and keeps the result in memory, so
// that the web page can ask for one page at a time.
//
// Why the whole report is rendered, not just the page asked for: the page count
// ("Page 3 of 34"), the totals of a group that started on an earlier page and the
// grand totals at the end are only known once every row has been laid out. What is
// NOT done for every page is the rest - the HTML of a page is written when that page
// is first asked for, and only that page goes over the network.
//
//   first request for a report + conditions   RetrieveAsync -> Render  (all pages, once)
//   every page request after that             HTML of that page        (from memory)
//   the PDF                                   PDF writer               (from memory, no second render)
//
// Two requests for the same report and conditions at the same moment share one
// render (Lazy<Task>), and at most MaxConcurrentRenders renders run at a time.

using System.Collections.Concurrent;
using System.Diagnostics;
using BluePrint.Render;
using BluePrint.Render.Models;
using BluePrint.Render.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ReportServerApi.Catalog;

namespace ReportServerApi.Rendering;

/// <summary>A report rendered for one set of conditions.</summary>
public sealed class RenderedReport(ReportEntry report, ReportArguments arguments, RenderedDocument document,
                                   TimeSpan elapsed, DateTimeOffset renderedAt)
{
    private readonly ConcurrentDictionary<(int Page, PageLayout Layout), byte[]> _pages = new();

    public ReportEntry Report { get; } = report;
    public ReportArguments Arguments { get; } = arguments;
    public RenderedDocument Document { get; } = document;
    public TimeSpan Elapsed { get; } = elapsed;
    public DateTimeOffset RenderedAt { get; } = renderedAt;

    /// <summary>The HTML of one page, written the first time it is asked for.</summary>
    public byte[] PageHtml(int page, PageLayout layout) =>
        _pages.GetOrAdd((page, layout), k => ReportOutput.PageHtml(Document, k.Page, k.Layout));
}

public sealed class RenderCache : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly SemaphoreSlim _slots;
    private readonly ReportServerOptions _settings;
    private readonly string _connectionString;
    private readonly ILogger<RenderCache> _log;

    public RenderCache(IOptions<ReportServerOptions> options, ILogger<RenderCache> log)
    {
        _settings = options.Value;
        _log = log;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = Math.Max(1, _settings.CacheSize) });
        _slots = new SemaphoreSlim(Math.Max(1, _settings.MaxConcurrentRenders));
        _connectionString = OptionLists.ConnectionString(ReportServerOptions.Resolve(_settings.Database));
    }

    /// <summary>The rendered report, from memory when it is there.</summary>
    public async Task<RenderedReport> GetAsync(ReportEntry report, ReportArguments arguments, CancellationToken ct) =>
        (await GetWithSourceAsync(report, arguments, ct)).Report;

    /// <summary>
    /// The rendered report, and whether it was already in memory (or being rendered for
    /// another request) - the web page shows which.
    /// </summary>
    public async Task<(RenderedReport Report, bool FromMemory)> GetWithSourceAsync(
        ReportEntry report, ReportArguments arguments, CancellationToken ct)
    {
        string key = report.Id + "|" + arguments.Key;
        bool created = false;
        var lazy = _cache.GetOrCreate(key, entry =>
        {
            created = true;
            entry.Size = 1;
            entry.SlidingExpiration = TimeSpan.FromMinutes(Math.Max(1, _settings.CacheMinutes));
            return new Lazy<Task<RenderedReport>>(() => RenderAsync(report, arguments));
        })!;

        try
        {
            // The render is shared by everybody who asks for it, so one browser that
            // goes away does not cancel it for the others - only its own wait ends.
            var result = await lazy.Value.WaitAsync(ct);
            return (result, !created);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A failed render is not kept: the next request tries again.
            _cache.Remove(key);
            throw;
        }
    }

    private async Task<RenderedReport> RenderAsync(ReportEntry report, ReportArguments arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, _settings.RenderTimeoutSeconds)));
        await _slots.WaitAsync(timeout.Token);
        try
        {
            var watch = Stopwatch.StartNew();

            var document = BluePrintDocument.Load(report.FilePath);
            await using (var connection = new SqliteConnection(_connectionString))
                await document.RetrieveAsync(connection, arguments.Values, ct: timeout.Token);

            // One measurer for every render: it caches the widths of the text it has
            // measured, and it is thread-safe.
            var rendered = document.Render(measurer: BpFonts.DefaultMeasurer, ct: timeout.Token)
                ?? throw new InvalidOperationException("The report rendered nothing.");

            watch.Stop();
            _log.LogInformation("Rendered {Report} [{Key}]: {Pages} pages, {Rows} rows, {Ms} ms",
                report.Id, arguments.Key, rendered.TotalPages, rendered.RowCount, watch.ElapsedMilliseconds);

            return new RenderedReport(report, arguments, rendered, watch.Elapsed, DateTimeOffset.Now);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        _slots.Dispose();
    }
}
