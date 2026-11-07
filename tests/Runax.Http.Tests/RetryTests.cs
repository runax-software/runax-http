using Runax.Http.Requests;
using Runax.Http.Retry;
using Runax.Http.Serialization;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// Retrying: off until asked for, and cautious once it is.
/// </summary>
/// <remarks>
/// Every test here runs with the delay set to zero. The backoff curve itself is a pure function
/// and is tested directly in <see cref="RetryPlannerTests"/>, so the loop can be exercised without
/// any test actually waiting.
/// </remarks>
public class RetryTests
{
    private const string Body = """{"name":"n","stars":0}""";

    /// <summary>Answers with the given statuses in order, then repeats the last one.</summary>
    private static Func<TransportRequest, TransportResponse> Sequence(params int[] statuses)
    {
        var call = 0;

        return _ =>
        {
            var status = statuses[Math.Min(call++, statuses.Length - 1)];

            return status is >= 200 and <= 299
                ? Responses.Json(Body, status)
                : Responses.Error(status);
        };
    }

    private static ClientHarness Harness(
        Func<TransportRequest, TransportResponse> respond,
        Action<RetryOptions>? retry = null)
        => ClientHarness.Create(
            respond,
            configure: client => client.WithRetry(options =>
            {
                options.MaxAttempts = 3;
                options.Delay = TimeSpan.Zero;
                options.Backoff = BackoffStrategy.Constant;
                options.UseJitter = false;

                retry?.Invoke(options);
            }));

    [Fact]
    public async Task Retrying_is_off_unless_asked_for()
    {
        var harness = ClientHarness.Create(Sequence(503, 200));

        await Should.ThrowAsync<HttpApiException>(() => harness.Client.GetAsync("owner", "name"));

        // One attempt. A client that retries without being told to can turn one slow dependency
        // into an outage, so this is the default and has to stay the default.
        harness.Transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_transient_status_is_retried_until_it_succeeds()
    {
        var harness = Harness(Sequence(503, 503, 200));

        var repository = await harness.Client.GetAsync("owner", "name");

        repository.Name.ShouldBe("n");
        harness.Transport.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Attempts_are_capped_and_the_last_response_is_what_surfaces()
    {
        var harness = Harness(Sequence(503));

        var thrown = await Should.ThrowAsync<HttpApiException>(
            () => harness.Client.GetAsync("owner", "name"));

        thrown.StatusCode.ShouldBe(503);
        harness.Transport.Requests.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task The_default_retryable_codes_are_the_transient_ones(int status)
    {
        var harness = Harness(Sequence(status, 200));

        await harness.Client.GetAsync("owner", "name");

        harness.Transport.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(500)]
    public async Task A_status_that_will_not_change_on_its_own_is_not_retried(int status)
    {
        // 500 is in this list on purpose. A plain Internal Server Error is usually a bug in the
        // request or the handler, so repeating it just multiplies the log noise.
        var harness = Harness(Sequence(status, 200));

        await Should.ThrowAsync<HttpApiException>(() => harness.Client.GetAsync("owner", "name"));

        harness.Transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_non_idempotent_method_is_not_retried_by_default()
    {
        // The most consequential default here. A retried POST that timed out after the server
        // committed it creates the thing twice, and the client cannot tell which happened.
        var harness = Harness(Sequence(503, 200));

        await Should.ThrowAsync<HttpApiException>(
            () => harness.Client.CreateIssueAsync("owner", "name", new NewIssue("t")));

        harness.Transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_non_idempotent_method_is_retried_when_explicitly_allowed()
    {
        var harness = Harness(
            Sequence(503, 200),
            options => options.RetryNonIdempotentMethods = true);

        await harness.Client.CreateIssueAsync("owner", "name", new NewIssue("t"));

        harness.Transport.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_idempotent_write_is_retried()
    {
        // PUT and DELETE are idempotent by definition, so they are in from the start.
        var harness = Harness(Sequence(503, 204));

        await harness.Client.DeleteAsync("owner", "name");

        harness.Transport.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_request_that_never_completed_is_retried()
    {
        var call = 0;

        var harness = Harness(_ =>
        {
            if (call++ == 0)
                throw new TransportException("connection refused");

            return Responses.Json(Body);
        });

        var repository = await harness.Client.GetAsync("owner", "name");

        // This is the case retrying actually exists for: no status code was ever received.
        repository.Name.ShouldBe("n");
        harness.Transport.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_transport_failure_that_never_clears_still_surfaces()
    {
        var harness = Harness(_ => throw new TransportException("connection refused"));

        var thrown = await Should.ThrowAsync<TransportException>(
            () => harness.Client.GetAsync("owner", "name"));

        thrown.Message.ShouldContain("connection refused");
        harness.Transport.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_capability_gap_is_not_retried()
    {
        // Nothing about waiting and asking again will give a transport a capability it does not
        // have, so retrying it would just delay the same error three times over.
        var harness = ClientHarness.Create(
            Sequence(200),
            configure: client => client
                .WithTimeout(TimeSpan.FromSeconds(5))
                .WithRetry(options =>
                {
                    options.MaxAttempts = 3;
                    options.Delay = TimeSpan.Zero;
                }));

        await Should.ThrowAsync<TransportCapabilityException>(
            () => harness.Client.GetAsync("owner", "name"));

        harness.Transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_streamed_body_is_never_retried()
    {
        // The stream was consumed by the first attempt, so a second would send a truncated
        // request. Buffering it instead would mean holding an upload of unknown size in memory on
        // the off chance of a retry. Built directly rather than through a generated client, since
        // no attribute asks for a streamed body yet.
        using var stream = new MemoryStream([1, 2, 3]);

        var transport = new RecordingTransport(
            capabilities: TransportCapabilities.RequestStreaming,
            respond: _ => Responses.Error(503));

        var options = new RunaxClientOptions { BaseAddress = new Uri("https://api.example.com") };
        options.Retry.MaxAttempts = 3;
        options.Retry.Delay = TimeSpan.Zero;
        options.Retry.RetryNonIdempotentMethods = true;

        var executor = new HttpContractExecutor(
            options,
            new HttpTransportResolver([transport]),
            SystemTextJsonContentSerializer.CreateReflectionBased());

        await Should.ThrowAsync<HttpApiException>(() => executor.SendAsync(
            new ClientRequest
            {
                Method = RequestMethod.Put,
                Template = "/upload",
                Content = RequestContent.FromStream(stream)
            }));

        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_After_in_seconds_is_honoured()
    {
        var call = 0;

        var harness = Harness(_ =>
        {
            if (call++ == 0)
            {
                var response = new TransportResponse(429)
                {
                    Headers = [new KeyValuePair<string, string>("Retry-After", "0")]
                };

                return response;
            }

            return Responses.Json(Body);
        });

        await harness.Client.GetAsync("owner", "name");

        harness.Transport.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_Retry_After_longer_than_we_will_wait_ends_the_attempts()
    {
        var harness = Harness(
            _ => new TransportResponse(429)
            {
                Headers = [new KeyValuePair<string, string>("Retry-After", "3600")]
            },
            options => options.MaxRetryAfterDelay = TimeSpan.FromSeconds(5));

        var thrown = await Should.ThrowAsync<HttpApiException>(
            () => harness.Client.GetAsync("owner", "name"));

        // Treated as a refusal to retry rather than an instruction to block for an hour.
        thrown.StatusCode.ShouldBe(429);
        harness.Transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_stops_the_attempts()
    {
        using var cancellation = new CancellationTokenSource();

        var harness = Harness(_ =>
        {
            cancellation.Cancel();
            throw new TransportException("connection refused");
        });

        await Should.ThrowAsync<TransportException>(
            () => harness.Client.GetAsync("owner", "name", cancellation.Token));

        harness.Transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void WithRetry_refuses_a_nonsensical_attempt_count()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => ClientHarness.Create(configure: client => client.WithRetry(0)));
}
