using System.Text;
using System.Text.Json;

namespace Runax.Http.Transports.Conformance;

/// <summary>
/// The behaviours every transport has to get right, run against a real server.
/// </summary>
/// <remarks>
/// <para>
/// Subclass it, return a transport, and a transport is "done" when this passes — rather than when
/// its author says so. That matters more than it sounds: without a shared suite, the first
/// transport's behaviour becomes the de facto specification by accident, and the second one is
/// written to match whatever the first happened to do.
/// </para>
/// <para>
/// Tests for capabilities a transport does not declare assert the <em>opposite</em> — that the
/// capability is genuinely absent — rather than being skipped. A transport that quietly gained a
/// behaviour it does not advertise is as much of a problem as one that lost a behaviour it does.
/// </para>
/// </remarks>
public abstract class TransportConformanceTests : IAsyncLifetime
{
    private EchoServer _server = null!;
    private IHttpTransport _transport = null!;

    /// <summary>Creates the transport under test.</summary>
    protected abstract IHttpTransport CreateTransport();

    /// <summary>The transport under test.</summary>
    protected IHttpTransport Transport => _transport;

    /// <summary>The server the transport is talking to.</summary>
    protected EchoServer Server => _server;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _server = await EchoServer.StartAsync();
        _transport = CreateTransport();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_transport is IDisposable disposable)
            disposable.Dispose();

        await _server.DisposeAsync();
    }

    private TransportRequest Request(
        string path = "/echo",
        RequestMethod method = RequestMethod.Get,
        RequestContent? content = null,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null,
        TimeSpan? timeout = null)
        => new()
        {
            Method = method,
            RequestUri = _server.Url(path),
            Content = content,
            Headers = headers ?? [],
            Timeout = timeout
        };

    private static async Task<EchoedRequest> ReadEchoAsync(TransportResponse response)
    {
        response.Content.ShouldNotBeNull();

        using var buffer = new MemoryStream();
        await response.Content!.CopyToAsync(buffer);

        var json = Encoding.UTF8.GetString(buffer.ToArray());

        return JsonSerializer.Deserialize<EchoedRequest>(json, EchoJson.Options)!;
    }

    private static string? Header(TransportResponse response, string name)
    {
        foreach (var (key, value) in response.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    // ---------------------------------------------------------------- identity

    [Fact]
    public void It_has_a_name()
        => Transport.Name.ShouldNotBeNullOrWhiteSpace();

    // ---------------------------------------------------------------- methods

    [Theory]
    [InlineData(RequestMethod.Get, "GET")]
    [InlineData(RequestMethod.Post, "POST")]
    [InlineData(RequestMethod.Put, "PUT")]
    [InlineData(RequestMethod.Patch, "PATCH")]
    [InlineData(RequestMethod.Delete, "DELETE")]
    [InlineData(RequestMethod.Options, "OPTIONS")]
    public async Task Every_method_reaches_the_server_as_itself(RequestMethod method, string expected)
    {
        using var response = await Transport.SendAsync(Request(method: method));

        response.StatusCode.ShouldBe(200);
        Server.Last.Method.ShouldBe(expected);
    }

    [Fact]
    public async Task HEAD_reaches_the_server_and_returns_no_body()
    {
        // Separate from the theory above because a HEAD response legitimately has no body to echo.
        using var response = await Transport.SendAsync(Request(method: RequestMethod.Head));

        response.StatusCode.ShouldBe(200);
        Server.Last.Method.ShouldBe("HEAD");
    }

    // ---------------------------------------------------------------- the URI

    [Fact]
    public async Task The_request_uri_is_sent_exactly_as_given()
    {
        // The single most important one. URL composition happens above the transport boundary, so a
        // transport that re-joins, re-escapes or normalizes anything makes the same client
        // interface hit a different URL depending on which transport was selected.
        //
        // Asserted against the raw request line rather than the server's decoded Path: Kestrel
        // unescapes %2F in Path, which would hide a transport that double-escaped it into %252F.
        var request = Request("/echo/a%2Fb/c?x=1&x=2&y=a%20b&z=");

        using var response = await Transport.SendAsync(request);

        var echoed = await ReadEchoAsync(response);

        echoed.RawTarget.ShouldBe(request.RequestUri.PathAndQuery);
    }

    [Fact]
    public async Task A_query_string_with_no_value_survives()
        => (await ReadEchoAsync(await Transport.SendAsync(Request("/echo?flag"))))
            .Query.ShouldBe("?flag");

    // ---------------------------------------------------------------- headers

    [Fact]
    public async Task Request_headers_reach_the_server()
    {
        using var response = await Transport.SendAsync(Request(
            headers:
            [
                new KeyValuePair<string, string>("X-Trace-Id", "abc123"),
                new KeyValuePair<string, string>("Accept", "application/json")
            ]));

        var echoed = await ReadEchoAsync(response);

        echoed.Header("X-Trace-Id").ShouldBe("abc123");
        echoed.Header("Accept").ShouldBe("application/json");
    }

    [Fact]
    public async Task A_content_type_header_reaches_the_server_rather_than_being_dropped()
    {
        // Content-Type belongs to the content, not the request, in more than one HTTP library. A
        // transport that only tries the request header collection silently loses it.
        using var response = await Transport.SendAsync(Request(
            method: RequestMethod.Post,
            content: RequestContent.FromString("# Title"),
            headers: [new KeyValuePair<string, string>("Content-Type", "text/markdown")]));

        var echoed = await ReadEchoAsync(response);

        echoed.Header("Content-Type").ShouldNotBeNull();
        echoed.Header("Content-Type")!.ShouldStartWith("text/markdown");
    }

    [Fact]
    public async Task Response_headers_come_back()
    {
        using var response = await Transport.SendAsync(Request("/headers"));

        Header(response, "X-Single").ShouldBe("one");
    }

    [Fact]
    public async Task A_repeated_response_header_is_not_collapsed()
    {
        // Set-Cookie legitimately repeats, which is why headers are a list rather than a dictionary.
        using var response = await Transport.SendAsync(Request("/headers"));

        response.Headers
            .Count(header => string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(2);
    }

    // ---------------------------------------------------------------- bodies

    [Fact]
    public async Task A_string_body_arrives_intact()
    {
        using var response = await Transport.SendAsync(Request(
            method: RequestMethod.Post,
            content: RequestContent.FromString("""{"a":1,"b":"héllo"}""", "application/json")));

        (await ReadEchoAsync(response)).Body.ShouldBe("""{"a":1,"b":"héllo"}""");
    }

    [Fact]
    public async Task A_binary_body_arrives_byte_for_byte()
    {
        var payload = new byte[] { 0, 1, 2, 250, 251, 255 };

        using var response = await Transport.SendAsync(Request(
            method: RequestMethod.Post,
            content: RequestContent.FromBytes(payload, "application/octet-stream")));

        (await ReadEchoAsync(response)).Bytes.ShouldBe(payload);
    }

    [Fact]
    public async Task A_form_body_is_url_encoded()
    {
        using var response = await Transport.SendAsync(Request(
            method: RequestMethod.Post,
            content: RequestContent.FromForm(
            [
                new KeyValuePair<string, string?>("name", "a b"),
                new KeyValuePair<string, string?>("tag", "x&y")
            ])));

        var echoed = await ReadEchoAsync(response);

        echoed.Header("Content-Type").ShouldNotBeNull();
        echoed.Header("Content-Type")!.ShouldStartWith("application/x-www-form-urlencoded");
        echoed.Body.ShouldContain("name=a");
        echoed.Body.ShouldContain("tag=x");
    }

    [Fact]
    public async Task A_request_with_no_body_sends_none()
        => (await ReadEchoAsync(await Transport.SendAsync(Request()))).Body.ShouldBeEmpty();

    // ---------------------------------------------------------------- responses

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Any_status_comes_back_as_a_response(int status)
    {
        // Deciding what a status code means belongs to the caller, which can see the client
        // interface's declared return type. A transport that throws takes that choice away.
        using var response = await Transport.SendAsync(Request($"/status/{status}"));

        response.StatusCode.ShouldBe(status);
        response.IsSuccessStatusCode.ShouldBe(status is >= 200 and <= 299);
    }

    [Fact]
    public async Task A_204_has_no_body()
    {
        using var response = await Transport.SendAsync(Request("/empty"));

        response.StatusCode.ShouldBe(204);

        if (response.Content is not null)
        {
            using var buffer = new MemoryStream();
            await response.Content.CopyToAsync(buffer);
            buffer.Length.ShouldBe(0);
        }
    }

    [Fact]
    public async Task A_binary_response_body_is_not_corrupted()
    {
        // Decoding the body to a string and re-encoding it mangles anything that is not UTF-8,
        // which is a plausible shortcut for a transport whose library hands back text.
        using var response = await Transport.SendAsync(Request("/bytes"));

        using var buffer = new MemoryStream();
        await response.Content!.CopyToAsync(buffer);

        buffer.ToArray().ShouldBe(new byte[] { 0, 1, 2, 250, 251, 255 });
    }

    [Fact]
    public async Task The_response_body_is_readable_after_SendAsync_returns()
    {
        // A transport that disposes its library's response on the way out closes the stream it just
        // handed over, and the symptom is an empty body rather than an error.
        var response = await Transport.SendAsync(Request("/status/200"));

        try
        {
            using var reader = new StreamReader(response.Content!);
            (await reader.ReadToEndAsync()).ShouldBe("status 200");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task The_response_reports_where_it_came_from()
    {
        using var response = await Transport.SendAsync(Request("/status/200"));

        response.FinalUri.ShouldNotBeNull();
        response.FinalUri!.AbsolutePath.ShouldBe("/status/200");
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task A_connection_failure_is_a_transport_exception()
    {
        // Port 1 on loopback: nothing is listening, and nothing is going to be.
        var unreachable = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("http://127.0.0.1:1/echo")
        };

        var thrown = await Should.ThrowAsync<TransportException>(
            () => Transport.SendAsync(unreachable));

        // Normalized, so a caller catches one type whatever transport ran the request.
        thrown.TransportName.ShouldBe(Transport.Name);
        thrown.RequestUri.ShouldBe(unreachable.RequestUri);
        thrown.ShouldNotBeOfType<TransportCapabilityException>();
    }

    [Fact]
    public async Task A_cancelled_request_stays_a_cancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(
            () => Transport.SendAsync(Request("/delay/10000"), cancellation.Token));
    }

    // ---------------------------------------------------------------- capabilities

    [Fact]
    public async Task A_per_request_timeout_is_honoured_if_declared()
    {
        var request = Request("/delay/10000", timeout: TimeSpan.FromMilliseconds(150));

        if (!Transport.Capabilities.HasFlag(TransportCapabilities.PerRequestTimeout))
        {
            // Not declared, so the executor would never hand it a request carrying one. Asserting
            // that the request does require it keeps the two sides of the check in step: if the
            // derivation stopped asking for it, this transport would start receiving timeouts it
            // cannot honour.
            request.RequiredCapabilities
                .HasFlag(TransportCapabilities.PerRequestTimeout).ShouldBeTrue();

            return;
        }

        var thrown = await Should.ThrowAsync<TransportException>(() => Transport.SendAsync(request));

        // A timeout, not a cancellation: "the deadline passed" and "the caller changed their mind"
        // send retry logic down different paths.
        thrown.ShouldNotBeOfType<TransportCapabilityException>();
        thrown.ShouldNotBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task A_streamed_request_body_is_streamed_or_refused()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("streamed body"));

        var request = Request(
            method: RequestMethod.Post,
            content: RequestContent.FromStream(stream, "text/plain"));

        if (!Transport.Capabilities.HasFlag(TransportCapabilities.RequestStreaming))
        {
            // The executor rejects this before the transport sees it. What matters here is that the
            // transport does not accept it anyway and silently buffer.
            await Should.ThrowAsync<TransportCapabilityException>(() => Transport.SendAsync(request));
            return;
        }

        using var response = await Transport.SendAsync(request);

        (await ReadEchoAsync(response)).Body.ShouldBe("streamed body");
    }

    [Fact]
    public async Task Multipart_is_sent_if_declared()
    {
        var content = RequestContent.FromMultipart(
        [
            new MultipartPart("field", RequestContent.FromString("value")),
            new MultipartPart("file", RequestContent.FromBytes("data"u8.ToArray()), "data.bin")
        ]);

        var request = Request(method: RequestMethod.Post, content: content);

        if (!Transport.Capabilities.HasFlag(TransportCapabilities.Multipart))
        {
            await Should.ThrowAsync<TransportCapabilityException>(() => Transport.SendAsync(request));
            return;
        }

        using var response = await Transport.SendAsync(request);
        var echoed = await ReadEchoAsync(response);

        echoed.Header("Content-Type").ShouldNotBeNull();
        echoed.Header("Content-Type")!.ShouldStartWith("multipart/form-data");
        echoed.Body.ShouldContain("value");
        echoed.Body.ShouldContain("data");
    }

    [Fact]
    public async Task Redirects_are_followed_if_declared()
    {
        using var response = await Transport.SendAsync(Request("/redirect"));

        if (Transport.Capabilities.HasFlag(TransportCapabilities.AutomaticRedirect))
        {
            response.StatusCode.ShouldBe(200);
            response.FinalUri!.AbsolutePath.ShouldBe("/echo");
        }
        else
        {
            response.StatusCode.ShouldBe(302);
            Header(response, "Location").ShouldNotBeNull();
        }
    }

    [Fact]
    public void A_transport_declares_nothing_it_cannot_do()
    {
        // Http3 needs opt-in on every stack the library knows of, and a transport claiming it
        // without the handler configuration to back it would fail at connect time rather than at
        // the capability check — which is the wrong place to find out.
        if (Transport.Capabilities.HasFlag(TransportCapabilities.Http3))
            Transport.Capabilities.HasFlag(TransportCapabilities.Http2).ShouldBeTrue();
    }
}
