// ReportServerWeb - serves the web page (wwwroot) and passes /api on to ReportServerAPI.
//
// The setting "ReportServerApi" is where the API is: host:port, or a full address.
//   on one machine      localhost:5081        (appsettings.json)
//   in docker compose   api:8080              (the name of the service)
//
// Only GET and HEAD go through: the page never changes anything on the server.

var builder = WebApplication.CreateBuilder(args);

string apiSetting = builder.Configuration["ReportServerApi"] ?? "localhost:5081";
var apiAddress = new Uri(apiSetting.Contains("://") ? apiSetting : "http" + "://" + apiSetting);

builder.Services.AddHttpClient("api", client =>
{
    client.BaseAddress = apiAddress;
    // The first request for a big report waits for the whole render.
    client.Timeout = TimeSpan.FromMinutes(3);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Always ask whether a file changed (answered with 304 when it did not), so a new
    // version of the page is picked up without clearing the browser's cache.
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
});

// Headers that describe the connection, not the content, and are not passed on.
string[] hopByHop = ["Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection"];

app.MapMethods("/api/{**path}", ["GET", "HEAD"], async (HttpContext context, IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("api");
    string target = context.Request.Path + context.Request.QueryString;
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

    HttpResponseMessage response;
    try
    {
        response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogWarning("The API at {Address} did not answer: {Message}", apiAddress, ex.Message);
        await Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "The report server is not reachable",
            detail: $"ReportServerWeb could not reach ReportServerAPI at {apiAddress.Authority}. " +
                    "Start ReportServerAPI, or set ReportServerApi to where it runs.").ExecuteAsync(context);
        return;
    }

    using (response)
    {
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (hopByHop.Contains(header.Key, StringComparer.OrdinalIgnoreCase)) continue;
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
});

app.Run();
