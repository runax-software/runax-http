using System.Net;
using System.Text;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// The built-in transport, against a stub handler rather than a network.
/// </summary>
public class HttpClientTransportTests
{
    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal HttpRequestMessage? Last { get; private set; }

        internal string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Last = request;

            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return await respond(request, cancellationToken);
        }
    }

    private static TransportRequest Request(
        RequestMethod method = RequestMethod.Get,
        string uri = "https://api.example.com/v3/things",
        RequestContent? content = null,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null,
        TimeSpan? timeout = null)
        => new()
        {
            Method = method,
            RequestUri = new Uri(uri),
            Content = content,
            Headers = headers ?? [],
            Timeout = timeout
        };

    private static (HttpClientTransport Transport, StubHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
        => BuildAsync((request, _) => Task.FromResult(
            respond is null ? new HttpResponseMessage(HttpStatusCode.OK) : respond(request)));

    /// <summary>
    /// For responders that need to await. Kept separate from <see cref="Build"/> rather than
    /// overloaded, because an overload taking a Task-returning lambda is easy to bind to by
    /// accident and would then be blocked on the calling thread — which would quietly defeat the
    /// timeout and cancellation tests below.
    /// </summary>
    private static (HttpClientTransport Transport, StubHandler Handler) BuildAsync(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);

        return (new HttpClientTransport(new HttpClient(handler)), handler);
    }

    [Fact]
    public void It_declares_what_a_stock_handler_can_do()
    {
        var (transport, _) = Build();

        transport.Name.ShouldBe("httpclient");
        transport.Capabilities.ShouldBe(HttpClientTransport.DefaultCapabilities);
    }

    [Fact]
    public void It_does_not_claim_a_per_request_proxy()
    {
        // A proxy belongs to the handler, which every request through this client shares. Claiming
        // it and then ignoring TransportRequest.Proxy would send a request direct that the caller
        // asked to route — the quiet failure the capability model exists to prevent.
        HttpClientTransport.DefaultCapabilities
            .HasFlag(TransportCapabilities.PerRequestProxy).ShouldBeFalse();
    }

    [Fact]
    public void It_does_not_claim_decompression_that_is_off_by_default()
        => HttpClientTransport.DefaultCapabilities
            .HasFlag(TransportCapabilities.AutomaticDecompression).ShouldBeFalse();

    [Theory]
    [InlineData(RequestMethod.Get, "GET")]
    [InlineData(RequestMethod.Post, "POST")]
    [InlineData(RequestMethod.Put, "PUT")]
    [InlineData(RequestMethod.Patch, "PATCH")]
    [InlineData(RequestMethod.Delete, "DELETE")]
    [InlineData(RequestMethod.Head, "HEAD")]
    [InlineData(RequestMethod.Options, "OPTIONS")]
    public async Task Every_method_maps_to_its_HttpMethod(RequestMethod method, string expected)
    {
        var (transport, handler) = Build();

        using var response = await transport.SendAsync(Request(method));

        handler.Last!.Method.Method.ShouldBe(expected);
    }

    [Fact]
    public async Task The_request_uri_is_used_as_given()
    {
        var (transport, handler) = Build();

        using var response = await transport.SendAsync(
            Request(uri: "https://api.example.com/v3/things?a=1&a=2"));

        handler.Last!.RequestUri!.ToString().ShouldBe("https://api.example.com/v3/things?a=1&a=2");
    }

    [Fact]
    public async Task A_json_body_is_sent_with_its_content_type()
    {
        var (transport, handler) = Build();

        using var response = await transport.SendAsync(Request(
            RequestMethod.Post,
            content: RequestContent.FromString("""{"a":1}""", "application/json")));

        handler.LastBody.ShouldBe("""{"a":1}""");
        handler.Last!.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task A_content_header_lands_on_the_content_rather_than_being_dropped()
    {
        // HttpClient rejects Content-Type on the request header collection, so a transport that
        // only tries there silently loses it.
        var (transport, handler) = Build();

        using var response = await transport.SendAsync(Request(
            RequestMethod.Put,
            content: RequestContent.FromString("# Title"),
            headers: [new KeyValuePair<string, string>("Content-Type", "text/markdown")]));

        handler.Last!.Content!.Headers.ContentType!.MediaType.ShouldBe("text/markdown");
    }

    [Fact]
    public async Task Request_headers_are_sent()
    {
        var (transport, handler) = Build();

        using var response = await transport.SendAsync(Request(
            headers: [new KeyValuePair<string, string>("X-Trace-Id", "abc123")]));

        handler.Last!.Headers.GetValues("X-Trace-Id").ShouldBe(["abc123"]);
    }

    [Fact]
    public async Task The_response_carries_status_headers_and_body()
    {
        var (transport, _) = Build(_ =>
        {
            var message = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json")
            };

            message.Headers.Add("X-Rate-Limit", "99");

            return message;
        });

        using var response = await transport.SendAsync(Request(RequestMethod.Post));

        response.StatusCode.ShouldBe(201);
        response.IsSuccessStatusCode.ShouldBeTrue();
        response.Headers.ShouldContain(header => header.Key == "X-Rate-Limit" && header.Value == "99");
        response.Headers.ShouldContain(header => header.Key == "Content-Type");

        using var reader = new StreamReader(response.Content!);
        (await reader.ReadToEndAsync()).ShouldBe("""{"id":1}""");
    }

    [Fact]
    public async Task An_error_status_comes_back_as_a_response_rather_than_an_exception()
    {
        // Deciding what a status code means belongs to the caller, which can see the client
        // interface's declared return type. A transport that throws takes that choice away.
        var (transport, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var response = await transport.SendAsync(Request());

        response.StatusCode.ShouldBe(404);
        response.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task The_response_body_is_still_readable_after_the_send_returns()
    {
        // The HttpResponseMessage is handed over as the response's transport resource rather than
        // disposed on the way out; disposing it there would close the stream just handed to the
        // caller, and the symptom would be an empty body rather than an error.
        var (transport, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("still here")
        });

        using var response = await transport.SendAsync(Request());

        using var reader = new StreamReader(response.Content!);
        (await reader.ReadToEndAsync()).ShouldBe("still here");
    }

    [Fact]
    public async Task A_connection_failure_surfaces_as_a_transport_failure()
    {
        var (transport, _) = Build(_ => throw new HttpRequestException("name resolution failed"));

        var thrown = await Should.ThrowAsync<TransportException>(() => transport.SendAsync(Request()));

        thrown.TransportName.ShouldBe("httpclient");
        thrown.RequestUri!.ToString().ShouldBe("https://api.example.com/v3/things");
        thrown.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task A_per_request_timeout_is_reported_as_a_timeout_not_a_cancellation()
    {
        // Surfacing our own deadline as OperationCanceledException would read as "the caller
        // changed their mind", which sends retry logic down the wrong path.
        var (transport, _) = BuildAsync(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var thrown = await Should.ThrowAsync<TransportException>(
            () => transport.SendAsync(Request(timeout: TimeSpan.FromMilliseconds(50))));

        thrown.Message.ShouldContain("timed out");
        thrown.ShouldNotBeOfType<TransportCapabilityException>();
    }

    [Fact]
    public async Task The_callers_cancellation_stays_a_cancellation()
    {
        var (transport, _) = BuildAsync(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            () => transport.SendAsync(Request(timeout: TimeSpan.FromSeconds(30)), cancellation.Token));
    }
}
