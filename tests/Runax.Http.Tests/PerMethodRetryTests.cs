using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Retry;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// A method's <c>[Retry]</c>, and how it resolves against what the client was registered with.
/// </summary>
/// <remarks>
/// <para>
/// The precedence rule is: <strong>a method's declaration refines the client's settings and wins
/// on what it names; <c>WithoutRetries()</c> beats everything.</strong>
/// </para>
/// <para>
/// Pinned down here rather than left to whatever the implementation happens to do, because the
/// rule is not self-evident — <see cref="TransportAttribute"/> resolves the other way round, with
/// registration winning. The difference is that a transport is a competing claim about one fact,
/// so one side has to lose, while a retry count is a refinement of a default. Every test below
/// would fail if the precedence silently flipped.
/// </para>
/// </remarks>
public class PerMethodRetryTests
{
    private const string Body = """{"name":"n","stars":0}""";

    /// <summary>Answers with the given statuses in order, then repeats the last.</summary>
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

    private static (IFlakyClient Client, RecordingTransport Transport) Build(
        Func<TransportRequest, TransportResponse> respond,
        Action<IRunaxClientBuilder<IFlakyClient>>? configure = null)
    {
        var transport = new RecordingTransport("recording", TransportCapabilities.None, respond);
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        var client = services.AddRunaxClient<IFlakyClient>();

        // Zero delay everywhere, so no test waits. The curve itself is covered by RetryPlannerTests.
        client.WithRetry(options =>
        {
            options.MaxAttempts = 1;
            options.Delay = TimeSpan.Zero;
            options.Backoff = BackoffStrategy.Constant;
            options.UseJitter = false;
        });

        configure?.Invoke(client);

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IFlakyClient>(), transport);
    }

    // -------------------------------------------------------- a declaration on its own

    [Fact]
    public async Task A_declaration_retries_without_the_operator_asking()
    {
        // The point of the attribute. If a client-wide WithRetry were needed first, the attribute
        // would do nothing in the common case — retrying is off by default — and be pointless.
        var (client, transport) = Build(Sequence(503, 503, 503, 200));

        await client.GetReportAsync(1);

        transport.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_method_that_declares_nothing_is_not_retried()
    {
        // The declaration is scoped to the method that carries it, not leaked to its neighbours.
        var (client, transport) = Build(Sequence(503, 200));

        await Should.ThrowAsync<HttpApiException>(() => client.ListReportsAsync());

        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_declared_delay_is_applied()
    {
        var (client, transport) = Build(Sequence(503, 200));

        await client.GetSlowReportAsync(1);

        transport.Requests.Count.ShouldBe(2);
    }

    // -------------------------------------------------------- against registration

    [Fact]
    public async Task A_declaration_beats_the_clients_attempt_count()
    {
        // The rule under test. An operator setting a client-wide default is not saying anything
        // about the one endpoint an author marked as flaky, so the specific declaration wins.
        var (client, transport) = Build(
            Sequence(503, 503, 503, 200),
            configure: builder => builder.WithRetry(2));

        await client.GetReportAsync(1);

        // Four, from [Retry(4)] — not two, from WithRetry(2).
        transport.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_declaration_of_one_attempt_opts_out_of_the_clients_policy()
    {
        // The same rule in the other direction: a method can be more cautious than its client.
        var (client, transport) = Build(
            Sequence(503, 200),
            configure: builder => builder.WithRetry(5));

        await Should.ThrowAsync<HttpApiException>(() => client.GetOnceAsync(1));

        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_declaration_inherits_everything_it_does_not_name()
    {
        // [Retry(4)] names an attempt count and nothing else, so the client's status-code set still
        // decides what is worth retrying. 500 is not retryable by default; adding it has to take
        // effect for the declared method too.
        var (client, transport) = Build(
            Sequence(500, 500, 200),
            configure: builder => builder.WithRetry(options =>
            {
                options.MaxAttempts = 1;
                options.Delay = TimeSpan.Zero;
                options.RetryableStatusCodes.Add(500);
            }));

        await client.GetReportAsync(1);

        transport.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_clients_settings_are_untouched_by_resolving_a_declaration()
    {
        // The resolved options are a copy. Mutating the client's set through them would make one
        // method's declaration quietly reconfigure every other method on the client.
        //
        // Always 503, so each call exhausts its own attempts rather than sharing a script.
        var (client, transport) = Build(
            Sequence(503),
            configure: builder => builder.WithRetry(2));

        await Should.ThrowAsync<HttpApiException>(() => client.GetReportAsync(1));
        transport.Requests.Count.ShouldBe(4);

        await Should.ThrowAsync<HttpApiException>(() => client.ListReportsAsync());

        // Two more from the client's own policy, not four.
        transport.Requests.Count.ShouldBe(6);
    }

    // -------------------------------------------------------- the kill switch

    [Fact]
    public async Task WithoutRetries_beats_a_declaration()
    {
        // The operator's last word. A client that cannot be stopped from retrying is a liability
        // in the middle of an incident, so this has to win over anything the source declared.
        var (client, transport) = Build(
            Sequence(503, 200),
            configure: builder => builder.WithoutRetries());

        await Should.ThrowAsync<HttpApiException>(() => client.GetReportAsync(1));

        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WithoutRetries_beats_a_declaration_and_a_client_policy_together()
    {
        var (client, transport) = Build(
            Sequence(503, 200),
            configure: builder => builder.WithRetry(5).WithoutRetries());

        await Should.ThrowAsync<HttpApiException>(() => client.GetReportAsync(1));

        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WithoutRetries_is_not_the_same_as_one_attempt()
    {
        // WithRetry(1) says this client has no policy of its own, which leaves a declaration free
        // to supply one. Collapsing the two would remove the kill switch.
        var (client, transport) = Build(
            Sequence(503, 503, 503, 200),
            configure: builder => builder.WithRetry(1));

        await client.GetReportAsync(1);

        transport.Requests.Count.ShouldBe(4);
    }

    // -------------------------------------------------------- the safety defaults

    [Fact]
    public async Task A_declaration_cannot_quietly_retry_a_non_idempotent_method()
    {
        // GetReportAsync is a GET; CreateReportAsync is a POST that opted in explicitly. A POST
        // that did not opt in is RXH020 at build time, so the only way here is the explicit one.
        var (client, transport) = Build(Sequence(503, 200));

        await client.CreateReportAsync(new NewIssue("t"));

        transport.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_explicit_opt_in_does_not_leak_to_other_methods()
    {
        // AllowNonIdempotent is folded into a copy of the options, so it applies to the method that
        // asked and nothing else.
        //
        // Always 503, so each call exhausts its own attempts rather than sharing a script.
        var (client, transport) = Build(Sequence(503));

        await Should.ThrowAsync<HttpApiException>(() => client.CreateReportAsync(new NewIssue("t")));
        transport.Requests.Count.ShouldBe(3);

        await Should.ThrowAsync<HttpApiException>(() => client.ListReportsAsync());
        transport.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public void A_streamed_body_is_never_retried_whatever_a_declaration_says()
    {
        // Not a policy the attribute can override: the first attempt consumed the stream, so a
        // second would send a truncated request.
        using var stream = new MemoryStream([1, 2, 3]);

        var request = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("https://api.example.com/upload"),
            Content = RequestContent.FromStream(stream)
        };

        var options = new RetryOptions { Delay = TimeSpan.Zero };
        var resolved = options.Resolve(new RetryOverride(5));

        resolved.MaxAttempts.ShouldBe(5);
        RetryPlanner.IsRetryable(resolved, request).ShouldBeFalse();
    }

    // -------------------------------------------------------- resolution, directly

    [Fact]
    public void Resolving_nothing_returns_the_clients_own_options()
    {
        var options = new RetryOptions { MaxAttempts = 3 };

        options.Resolve(null).ShouldBeSameAs(options);
    }

    [Fact]
    public void Resolving_while_suppressed_returns_the_clients_own_options()
    {
        var options = new RetryOptions { Suppressed = true, MaxAttempts = 1 };

        var resolved = options.Resolve(new RetryOverride(5));

        resolved.ShouldBeSameAs(options);
        resolved.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void A_resolved_copy_keeps_everything_the_declaration_did_not_name()
    {
        var options = new RetryOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromSeconds(3),
            MaxDelay = TimeSpan.FromSeconds(9),
            Backoff = BackoffStrategy.Linear,
            UseJitter = false,
            RespectRetryAfter = false,
            MaxRetryAfterDelay = TimeSpan.FromSeconds(11),
            RetryTransportFailures = false
        };

        options.RetryableStatusCodes.Add(500);

        var resolved = options.Resolve(new RetryOverride(7));

        resolved.MaxAttempts.ShouldBe(7);
        resolved.Delay.ShouldBe(TimeSpan.FromSeconds(3));
        resolved.MaxDelay.ShouldBe(TimeSpan.FromSeconds(9));
        resolved.Backoff.ShouldBe(BackoffStrategy.Linear);
        resolved.UseJitter.ShouldBeFalse();
        resolved.RespectRetryAfter.ShouldBeFalse();
        resolved.MaxRetryAfterDelay.ShouldBe(TimeSpan.FromSeconds(11));
        resolved.RetryTransportFailures.ShouldBeFalse();
        resolved.RetryableStatusCodes.ShouldContain(500);
    }

    [Fact]
    public void A_resolved_copy_does_not_share_the_status_code_set()
    {
        var options = new RetryOptions();
        var resolved = options.Resolve(new RetryOverride(3));

        resolved.RetryableStatusCodes.Add(418);

        options.RetryableStatusCodes.ShouldNotContain(418);
    }

    [Fact]
    public void A_declared_delay_of_zero_is_honoured_rather_than_read_as_absent()
    {
        // Why the sentinel is -1 and not 0: zero is a legitimate delay, and tests rely on it.
        var options = new RetryOptions { Delay = TimeSpan.FromSeconds(5) };

        options.Resolve(new RetryOverride(3, delayMilliseconds: 0))
            .Delay.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void An_inherited_delay_keeps_the_clients()
        => new RetryOptions { Delay = TimeSpan.FromSeconds(5) }
            .Resolve(new RetryOverride(3))
            .Delay.ShouldBe(TimeSpan.FromSeconds(5));

    [Fact]
    public void An_opt_in_is_additive_rather_than_a_replacement()
    {
        // A client that already allows non-idempotent retries keeps allowing them.
        new RetryOptions { RetryNonIdempotentMethods = true }
            .Resolve(new RetryOverride(3))
            .RetryNonIdempotentMethods.ShouldBeTrue();

        new RetryOptions()
            .Resolve(new RetryOverride(3, allowNonIdempotent: true))
            .RetryNonIdempotentMethods.ShouldBeTrue();
    }
}
