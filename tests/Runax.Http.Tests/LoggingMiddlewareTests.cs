using Microsoft.Extensions.Logging;
using Runax.Http.Pipeline;
using Runax.Http.Requests;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// What the logging stage writes, and — more importantly — what it refuses to.
/// </summary>
/// <remarks>
/// The redaction tests are the point of this file. A bearer token or a request body in an
/// application log is a credential leak that outlives the request by however long the log is
/// retained, so "safe by default" has to be a tested property rather than a claim in a doc comment.
/// </remarks>
public class LoggingMiddlewareTests
{
    private const string Secret = "super-secret-token";

    private static async Task<CapturingLogger> SendAsync(
        HttpLoggingOptions? options = null,
        int statusCode = 200,
        Exception? failWith = null,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null,
        RequestContent? content = null,
        Uri? uri = null)
    {
        var logger = new CapturingLogger();
        var middleware = new LoggingMiddleware(logger, options);

        var request = new TransportRequest
        {
            Method = RequestMethod.Post,
            RequestUri = uri ?? new Uri("https://api.example.com/v3/repos/o/n?state=open"),
            Headers = headers ?? [],
            Content = content
        };

        var clientRequest = new ClientRequest { Method = RequestMethod.Post, Template = "/repos" };
        var transport = new RecordingTransport("logging-test");
        var context = new RequestContext(clientRequest, request, transport);

        RequestPipelineStep terminal = (_, _) => failWith is not null
            ? Task.FromException<TransportResponse>(failWith)
            : Task.FromResult(new TransportResponse(statusCode));

        try
        {
            using var response = await middleware.InvokeAsync(context, terminal, CancellationToken.None);
        }
        catch (Exception exception) when (exception == failWith)
        {
            // Rethrown by the stage after logging, which is the behaviour under test.
        }

        return logger;
    }

    // ------------------------------------------------------------- redaction

    [Fact]
    public async Task No_headers_are_logged_by_default()
    {
        // An allow-list rather than a deny-list: a deny-list protects the headers someone thought
        // of, and the one that leaks is always the one nobody thought of.
        var logger = await SendAsync(
            headers: [new KeyValuePair<string, string>("Authorization", $"Bearer {Secret}")]);

        logger.AllText.ShouldNotContain(Secret);
        logger.AllText.ShouldNotContain("Authorization");
    }

    [Fact]
    public async Task An_allow_listed_Authorization_header_logs_its_presence_but_not_its_value()
    {
        // Answers "was authentication even attached?" without carrying the credential. There is no
        // option that produces the token itself.
        var options = new HttpLoggingOptions();
        options.LoggedHeaders.Add("Authorization");

        var logger = await SendAsync(
            options,
            headers: [new KeyValuePair<string, string>("Authorization", $"Bearer {Secret}")]);

        logger.AllText.ShouldContain("Authorization: ***");
        logger.AllText.ShouldNotContain(Secret);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    public async Task The_never_logged_headers_are_redacted_even_when_allow_listed(string name)
    {
        var options = new HttpLoggingOptions();
        options.LoggedHeaders.Add(name);

        var logger = await SendAsync(
            options,
            headers: [new KeyValuePair<string, string>(name, Secret)]);

        logger.AllText.ShouldNotContain(Secret);
        logger.AllText.ShouldContain($"{name}: ***");
    }

    [Fact]
    public async Task An_allow_listed_ordinary_header_logs_its_value()
    {
        var options = new HttpLoggingOptions();
        options.LoggedHeaders.Add("X-Request-Id");

        var logger = await SendAsync(
            options,
            headers: [new KeyValuePair<string, string>("X-Request-Id", "abc123")]);

        logger.AllText.ShouldContain("X-Request-Id: abc123");
    }

    [Fact]
    public async Task The_request_body_is_never_logged()
    {
        // Deliberately not an option. A body is the most likely place for a password or someone's
        // personal data, and a switch like that gets turned on in an incident and left on.
        var logger = await SendAsync(
            content: RequestContent.FromString($$"""{"password":"{{Secret}}"}""", "application/json"));

        logger.AllText.ShouldNotContain(Secret);
        logger.AllText.ShouldNotContain("password");
    }

    [Fact]
    public async Task The_query_string_can_be_left_out()
    {
        // For an API that takes a credential as a query parameter, which RXH005 exists partly to
        // stop happening by accident in the first place.
        var logger = await SendAsync(
            new HttpLoggingOptions { LogQueryString = false },
            uri: new Uri($"https://api.example.com/v3/repos?api_key={Secret}"));

        logger.AllText.ShouldNotContain(Secret);
        logger.AllText.ShouldContain("/v3/repos");
    }

    [Fact]
    public async Task The_query_string_is_logged_by_default()
    {
        var logger = await SendAsync();

        logger.AllText.ShouldContain("?state=open");
    }

    // ---------------------------------------------------------------- content

    [Fact]
    public async Task A_successful_response_logs_the_method_uri_status_transport_and_elapsed()
    {
        var logger = await SendAsync();

        var line = logger.Lines.Last();

        line.Level.ShouldBe(LogLevel.Information);
        line.Message.ShouldContain("POST");
        line.Message.ShouldContain("/v3/repos/o/n");
        line.Message.ShouldContain("200");
        line.Message.ShouldContain("logging-test");
        line.Message.ShouldContain("ms");
    }

    [Fact]
    public async Task An_error_status_is_logged_above_a_success()
    {
        // A response something treats as a fault should not sit at the level of one that worked.
        var logger = await SendAsync(statusCode: 500);

        logger.Lines.Last().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task A_transport_failure_is_logged_with_the_exception_and_rethrown()
    {
        var failure = new TransportException("connection refused");

        var logger = await SendAsync(failWith: failure);

        var line = logger.Lines.Last();
        line.Level.ShouldBe(LogLevel.Error);
        line.Exception.ShouldBeSameAs(failure);
        line.Message.ShouldContain("failed");
    }

    [Fact]
    public async Task A_cancellation_is_not_logged_as_an_error()
    {
        // A cancelled shutdown would otherwise fill the log with errors for something nobody needs
        // to act on.
        var logger = new CapturingLogger();
        var middleware = new LoggingMiddleware(logger);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var context = new RequestContext(
            new ClientRequest { Method = RequestMethod.Get, Template = "/x" },
            new TransportRequest { Method = RequestMethod.Get, RequestUri = new Uri("https://h/x") },
            new RecordingTransport("logging-test"));

        await Should.ThrowAsync<OperationCanceledException>(() => middleware.InvokeAsync(
            context,
            (_, _) => Task.FromException<TransportResponse>(new OperationCanceledException()),
            cancellation.Token));

        logger.Lines.ShouldNotContain(line => line.Level >= LogLevel.Error);
        logger.AllText.ShouldContain("cancelled");
    }

    [Fact]
    public async Task A_slow_request_is_reported_even_when_it_succeeded()
    {
        var logger = await SendAsync(new HttpLoggingOptions
        {
            SlowRequestThreshold = TimeSpan.Zero,
            SlowRequestLevel = LogLevel.Warning
        });

        logger.Lines.Last().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task The_slow_threshold_never_lowers_a_level_that_is_already_higher()
    {
        // A 500 that was also slow is still a 500; the threshold may only raise.
        var logger = await SendAsync(
            new HttpLoggingOptions
            {
                SlowRequestThreshold = TimeSpan.Zero,
                SlowRequestLevel = LogLevel.Debug,
                ErrorStatusLevel = LogLevel.Warning
            },
            statusCode: 500);

        logger.Lines.Last().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task Nothing_is_rendered_when_the_level_is_disabled()
    {
        // The IsEnabled guards exist so a disabled level costs nothing; this proves they are there.
        var logger = new CapturingLogger(LogLevel.None);
        var middleware = new LoggingMiddleware(logger);

        var context = new RequestContext(
            new ClientRequest { Method = RequestMethod.Get, Template = "/x" },
            new TransportRequest { Method = RequestMethod.Get, RequestUri = new Uri("https://h/x") },
            new RecordingTransport("logging-test"));

        using var response = await middleware.InvokeAsync(
            context,
            (_, _) => Task.FromResult(new TransportResponse(200)),
            CancellationToken.None);

        logger.Lines.ShouldBeEmpty();
    }
}
