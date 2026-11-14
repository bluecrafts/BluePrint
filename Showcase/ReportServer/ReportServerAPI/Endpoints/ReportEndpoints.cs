// The HTTP API.
//
//   GET /api/reports                          the reports and their conditions
//   GET /api/reports/{id}/info?<conditions>    renders (or finds in memory) and tells the page count
//   GET /api/reports/{id}/pages/{n}?<conditions>&layout=paper|table
//                                              the HTML of page n - one page, not the whole report
//   GET /api/reports/{id}/pdf?<conditions>[&pages=3]
//                                              the PDF of every page, or of a page range
//
// <conditions> are the names of the report's conditions (?FromDate=1997-01-01&Country=USA);
// one that is left out takes its default. The same report with the same conditions
// is rendered once and kept in memory (RenderCache), so /info, every /pages/{n} and
// /pdf after it are served from that one render.
//
// Errors are answered as application/problem+json with a sentence in "detail" that
// the web page shows as it is.

using System.Globalization;
using BluePrint.Render.Printing;
using ReportServerApi.Catalog;
using ReportServerApi.Rendering;

namespace ReportServerApi.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/reports");

        api.MapGet("/", (ReportCatalog catalog) =>
            Results.Ok(catalog.All.Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.Orientation,
                r.Parameters,
            })));

        api.MapGet("/{id}/info", (string id, HttpRequest request, ReportCatalog catalog, RenderCache cache, CancellationToken ct) =>
            Handle(async () =>
            {
                var (report, arguments) = Resolve(id, request, catalog);
                var (rendered, fromMemory) = await cache.GetWithSourceAsync(report, arguments, ct);

                return Results.Ok(new
                {
                    report = report.Id,
                    report.Title,
                    conditions = arguments.Texts,
                    pageCount = rendered.Document.TotalPages,
                    rowCount = rendered.Document.RowCount,
                    // Page sizes in CSS pixels (96 per inch): the web page needs them to
                    // fit the page to the window and to set the paper size when printing.
                    pages = rendered.Document.Pages.Select(p => new { width = Math.Round(p.WidthPx, 2), height = Math.Round(p.HeightPx, 2) }),
                    renderMilliseconds = (long)rendered.Elapsed.TotalMilliseconds,
                    renderedAt = rendered.RenderedAt,
                    fromMemory,
                });
            }));

        api.MapGet("/{id}/pages/{page:int}", (string id, int page, HttpRequest request, HttpResponse response,
                                              ReportCatalog catalog, RenderCache cache, CancellationToken ct) =>
            Handle(async () =>
            {
                var (report, arguments) = Resolve(id, request, catalog);
                var layout = ParseLayout(request.Query["layout"]);
                var rendered = await cache.GetAsync(report, arguments, ct);

                int count = rendered.Document.TotalPages;
                if (page < 1 || page > count)
                    return Problem(StatusCodes.Status404NotFound, "No such page",
                        $"The report has {count} page{(count == 1 ? "" : "s")} for these conditions; page {page} is not one of them.");

                response.Headers["X-Page"] = page.ToString(CultureInfo.InvariantCulture);
                response.Headers["X-Page-Count"] = count.ToString(CultureInfo.InvariantCulture);
                return Results.Bytes(rendered.PageHtml(page, layout), "text/html; charset=utf-8");
            }));

        api.MapGet("/{id}/pdf", (string id, HttpRequest request, ReportCatalog catalog, RenderCache cache, CancellationToken ct) =>
            Handle(async () =>
            {
                var (report, arguments) = Resolve(id, request, catalog);
                string? pages = request.Query["pages"].ToString() is { Length: > 0 } p ? p.Trim() : null;
                var rendered = await cache.GetAsync(report, arguments, ct);

                if (pages is not null)
                {
                    try { BluePrintPageRange.Parse(pages); }
                    catch (Exception ex) when (ex is FormatException or ArgumentException)
                    {
                        throw new BadRequestException($"pages: '{pages}' is not a page range such as 3 or 1-5.");
                    }
                }

                byte[] pdf = ReportOutput.Pdf(rendered.Document, pages);
                return Results.File(pdf, "application/pdf", FileName(report, arguments, pages));
            }));
    }

    private static (ReportEntry Report, ReportArguments Arguments) Resolve(string id, HttpRequest request, ReportCatalog catalog)
    {
        var report = catalog.Find(id) ?? throw new NotFoundException($"There is no report '{id}'.");
        return (report, ReportArguments.From(report, request.Query));
    }

    private static PageLayout ParseLayout(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "paper" => PageLayout.Paper,
        "table" => PageLayout.Table,
        _ => throw new BadRequestException($"layout: '{text}' is neither paper nor table."),
    };

    // sales-by-customer_1997-01-01_1997-12-31_USA_page-3.pdf
    private static string FileName(ReportEntry report, ReportArguments arguments, string? pages)
    {
        var parts = new List<string> { report.Id };
        foreach (var p in report.Parameters)
        {
            string text = arguments.Texts[p.Name];
            if (p.Kind == ParameterKind.Select && text == p.DefaultValue) continue;   // "all" adds nothing
            string label = p.Options?.FirstOrDefault(o => o.Value == text)?.Label ?? text;
            parts.Add(new string(label.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray()));
        }
        if (pages is not null) parts.Add("page-" + pages);
        return string.Join("_", parts) + ".pdf";
    }

    private sealed class NotFoundException(string message) : Exception(message);

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BadRequestException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, "The conditions are not valid", ex.Message);
        }
        catch (NotFoundException ex)
        {
            return Problem(StatusCodes.Status404NotFound, "Not found", ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Either the browser went away (nobody reads this) or the render ran out of time.
            return Problem(StatusCodes.Status503ServiceUnavailable, "The report took too long",
                "The report was not ready in time. Narrow the conditions, or try again in a moment.");
        }
        catch (Exception ex)
        {
            // A sample shows the reason as it is. A server open to the internet would log
            // it and answer with a sentence that says less.
            return Problem(StatusCodes.Status500InternalServerError, "The report could not be made", ex.Message);
        }
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail);
}
