// ReportServerAPI - renders the Northwind reports and serves them one page at a time
// as HTML, or as a PDF. ReportServerWeb is the web page that shows them; it reaches
// this API through its own /api address, so a browser only ever talks to the web.
//
// Runs the same way on Windows, on Linux and in a container (see the Dockerfile).

using System.Text.Json;
using System.Text.Json.Serialization;
using BluePrint.Render.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using ReportServerApi;
using ReportServerApi.Catalog;
using ReportServerApi.Endpoints;
using ReportServerApi.Rendering;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ReportServerOptions>(builder.Configuration.GetSection(ReportServerOptions.Section));
builder.Services.AddSingleton<ReportCatalog>();
builder.Services.AddSingleton<RenderCache>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();
var settings = app.Services.GetRequiredService<IOptions<ReportServerOptions>>().Value;

// ---- fonts --------------------------------------------------------------------
// The reports are measured and drawn with the fonts in this folder, never with the
// fonts installed on the machine: a Linux container has none of the Windows fonts,
// and a page laid out with one font and shown with another overflows its boxes.
// The browser gets the same files (/api/fonts below), so the HTML matches too.
string fontDir = ReportServerOptions.Resolve(settings.Fonts);
if (!Directory.Exists(fontDir))
    throw new DirectoryNotFoundException($"The font folder is not there: {fontDir}");
BpFonts.Logger = (level, message) => app.Logger.Log(level == "warn" ? LogLevel.Warning : LogLevel.Information, "{Message}", message);
BpFonts.UseBundledFonts(fontDir, fallbackFamily: "Inter");
// On Windows BluePrint would prefer a font of the same name installed on the machine;
// turned off, so Windows, Linux and the browser all use exactly these files.
BluePrintPdf.BpFontPolicy.PreferInstalledFonts = false;

// Open the reports and read the lists now, so a missing file or a report that does
// not match its conditions stops the server at startup, not at the first request.
var catalog = app.Services.GetRequiredService<ReportCatalog>();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The fonts, and fonts.css that declares them, for the browser.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".ttf"] = "font/ttf";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(fontDir),
    RequestPath = "/api/fonts",
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public, max-age=86400",
});

app.MapReportEndpoints();
app.MapGet("/api/health", () => Results.Ok(new { status = "ready", reports = catalog.All.Count() }));

app.Run();

/// <summary>Visible to WebApplicationFactory in tests.</summary>
public partial class Program;
