using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Runax.Http.Transports.Conformance;

/// <summary>What the echo endpoint reports back about the request it received.</summary>
public sealed record EchoedRequest(
    string Method,
    string Path,
    string Query,
    string RawTarget,
    Dictionary<string, string[]> Headers,
    string BodyBase64)
{
    /// <summary>The request body as text.</summary>
    public string Body => Encoding.UTF8.GetString(Convert.FromBase64String(BodyBase64));

    /// <summary>The request body as bytes.</summary>
    public byte[] Bytes => Convert.FromBase64String(BodyBase64);

    /// <summary>A header's first value, or <see langword="null"/> if it was not sent.</summary>
    public string? Header(string name)
    {
        foreach (var (key, values) in Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return values.FirstOrDefault();
        }

        return null;
    }
}

/// <summary>
/// A real HTTP server on a loopback port, for exercising a transport against actual sockets.
/// </summary>
/// <remarks>
/// Kestrel rather than a stub <c>HttpMessageHandler</c>, because a stub only proves a transport can
/// talk to a stub. What this suite cares about — whether a header reached the wire, how a query
/// string was encoded, what a timeout actually does — is only observable against something that
/// really parses HTTP.
/// </remarks>
public sealed class EchoServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<EchoedRequest> _requests;

    private EchoServer(WebApplication app, Uri baseAddress, List<EchoedRequest> requests)
    {
        _app = app;
        _requests = requests;
        BaseAddress = baseAddress;
    }

    /// <summary>The address the server is listening on.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Starts a server on a free loopback port.</summary>
    /// <returns>The running server.</returns>
    public static async Task<EchoServer> StartAsync()
    {
        // Port 0, so parallel test classes each get their own server without colliding.
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var requests = new List<EchoedRequest>();

        app.Map("/{**path}", async (HttpContext context) =>
        {
            var echoed = await ReadAsync(context).ConfigureAwait(false);

            lock (requests)
                requests.Add(echoed);

            await RespondAsync(context, echoed).ConfigureAwait(false);
        });

        await app.StartAsync().ConfigureAwait(false);

        return new EchoServer(app, new Uri(app.Urls.First()), requests);
    }

    /// <summary>An address on this server.</summary>
    /// <param name="relative">A path, with or without a leading slash.</param>
    /// <returns>The absolute address.</returns>
    public Uri Url(string relative) => new(BaseAddress, relative);

    /// <summary>The request the server most recently received.</summary>
    public EchoedRequest Last
    {
        get
        {
            lock (_requests)
                return _requests[^1];
        }
    }

    /// <summary>How many requests the server has received.</summary>
    public int Count
    {
        get
        {
            lock (_requests)
                return _requests.Count;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<EchoedRequest> ReadAsync(HttpContext context)
    {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer).ConfigureAwait(false);

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in context.Request.Headers)
        {
            headers[header.Key] = header.Value
                .Where(value => value is not null)
                .Select(value => value!)
                .ToArray();
        }

        return new EchoedRequest(
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty,
            context.Request.QueryString.Value ?? string.Empty,

            // The request line as it arrived, before the server decoded anything. "Sent exactly as
            // given" is a claim about the wire, and Path is the server's interpretation of it —
            // Kestrel unescapes %2F there, which would hide a transport that double-escaped.
            context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty,
            headers,
            Convert.ToBase64String(buffer.ToArray()));
    }

    /// <summary>
    /// Routes on the path, so one endpoint covers every behaviour the suite needs to provoke.
    /// </summary>
    private static async Task RespondAsync(HttpContext context, EchoedRequest echoed)
    {
        var segments = (context.Request.Path.Value ?? string.Empty).Trim('/').Split('/');

        switch (segments.ElementAtOrDefault(0))
        {
            case "status" when int.TryParse(segments.ElementAtOrDefault(1), out var status):
                context.Response.StatusCode = status;

                if (status != 204 && status != 304)
                    await context.Response.WriteAsync($"status {status}").ConfigureAwait(false);

                return;

            case "delay" when int.TryParse(segments.ElementAtOrDefault(1), out var milliseconds):
                await Task.Delay(milliseconds, context.RequestAborted).ConfigureAwait(false);
                await context.Response.WriteAsync("delayed").ConfigureAwait(false);
                return;

            case "headers":
                context.Response.Headers["X-Single"] = "one";

                // Two values under one name, which is the case a dictionary-shaped header
                // collection silently loses.
                context.Response.Headers.Append("Set-Cookie", "a=1");
                context.Response.Headers.Append("Set-Cookie", "b=2");

                await context.Response.WriteAsync("ok").ConfigureAwait(false);
                return;

            case "redirect":
                context.Response.StatusCode = 302;
                context.Response.Headers.Location = "/echo";
                return;

            case "empty":
                context.Response.StatusCode = 204;
                return;

            case "bytes":
                // Deliberately not valid UTF-8, so a transport that decodes and re-encodes the body
                // corrupts it visibly.
                context.Response.ContentType = "application/octet-stream";
                await context.Response.Body
                    .WriteAsync(new byte[] { 0, 1, 2, 250, 251, 255 })
                    .ConfigureAwait(false);

                return;

            default:
                context.Response.ContentType = "application/json";
                await context.Response
                    .WriteAsync(JsonSerializer.Serialize(echoed, EchoJson.Options))
                    .ConfigureAwait(false);

                return;
        }
    }
}

/// <summary>Serializer settings shared by the server and whoever reads its output.</summary>
public static class EchoJson
{
    /// <summary>Web defaults, matching what the server writes.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
