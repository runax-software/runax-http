using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Retry;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// The budget that bounds a whole call, retries and backoff included.
/// </summary>
/// <remarks>
/// <see cref="RunaxClientOptions.Timeout"/> bounds an <em>attempt</em>, which reads like a call
/// budget and is not one: the transport request carrying it is built once, before the retry loop,
/// so every attempt gets the same allowance and the caller waits for all of them plus the waits
/// between. Five attempts at thirty seconds is over two and a half minutes with nothing in the
/// configuration looking wrong.
/// </remarks>
public class TotalTimeoutTests
{
    private static (IRepositoryClient Client, BlockingTransport Transport) Build(
        Func<TransportRequest, CancellationToken, Task<TransportResponse>> respond,
        Action<IRunaxClientBuilder<IRepositoryClient>> configure)
    {
        var transport = new BlockingTransport(respond);
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        configure(services.AddRunaxClient<IRepositoryClient>());

        return (services.BuildServiceProvider().GetRequiredService<IRepositoryClient>(), transport);
    }

    /// <summary>
    /// A transport that can take as long as a test needs and honours cancellation.
    /// </summary>
    /// <remarks>
    /// Standalone rather than built on <c>RecordingTransport</c>, which is sealed and answers
    /// synchronously — and a synchronous responder cannot be made to outrun a deadline.
    /// </remarks>
    private sealed class BlockingTransport(
        Func<TransportRequest, CancellationToken, Task<TransportResponse>> respond) : IHttpTransport
    {
        public string Name => "blocking";

        public TransportCapabilities Capabilities =>
            TransportCapabilities.CancellationMidFlight | TransportCapabilities.PerRequestTimeout;

        public List<TransportRequest> Requests { get; } = [];

        public Task<TransportResponse> SendAsync(
            TransportRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (Requests)
                Requests.Add(request);

            return respond(request, cancellationToken);
        }
    }

    [Fact]
    public async Task A_call_that_outruns_its_budget_fails_as_a_timeout_not_a_cancellation()
    {
        // The distinction matters: "the deadline passed" and "the caller changed their mind" lead
        // to different retry and alerting decisions.
        var (client, _) = Build(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Responses.Json("{}");
            },
            c => c.WithTotalTimeout(TimeSpan.FromMilliseconds(150)));

        var thrown = await Should.ThrowAsync<TransportException>(() => client.GetAsync("o", "n"));

        thrown.ShouldNotBeAssignableTo<OperationCanceledException>();
        thrown.Message.ShouldContain("total budget");
    }

    [Fact]
    public async Task The_budget_covers_the_retries_and_not_just_one_attempt()
    {
        // The whole point. Each attempt is fast and fails, so a per-attempt timeout would never
        // fire; only a call budget stops the loop.
        var attempts = 0;

        var (client, _) = Build(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(Responses.Error(503));
            },
            c => c
                .WithTotalTimeout(TimeSpan.FromMilliseconds(250))
                .WithRetry(options =>
                {
                    options.MaxAttempts = 100;
                    options.Delay = TimeSpan.FromMilliseconds(40);
                    options.Backoff = BackoffStrategy.Constant;
                    options.UseJitter = false;
                }));

        var started = Stopwatch.StartNew();

        await Should.ThrowAsync<TransportException>(() => client.GetAsync("o", "n"));

        started.Stop();

        // Stopped well short of a hundred attempts, and quickly. Generous bounds because CI
        // timing is not a precision instrument; the assertion is "bounded", not "exactly 250ms".
        attempts.ShouldBeLessThan(100);
        attempts.ShouldBeGreaterThan(1);
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_callers_own_cancellation_stays_a_cancellation()
    {
        // The budget must not swallow the caller's intent and relabel it a timeout.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var (client, _) = Build(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Responses.Json("{}");
            },
            c => c.WithTotalTimeout(TimeSpan.FromSeconds(30)));

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.GetAsync("o", "n", cancellation.Token));
    }

    [Fact]
    public async Task A_call_inside_its_budget_is_untouched()
    {
        var (client, transport) = Build(
            (_, _) => Task.FromResult(Responses.Json("""{"name":"n","stars":0}""")),
            c => c.WithTotalTimeout(TimeSpan.FromSeconds(30)));

        var repository = await client.GetAsync("o", "n");

        repository.Name.ShouldBe("n");
        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task No_budget_means_unbounded_as_before()
    {
        // The default has to stay what it was: adding a budget nobody asked for would change the
        // behaviour of every existing client on upgrade.
        var (client, _) = Build(
            (_, _) => Task.FromResult(Responses.Json("""{"name":"n","stars":0}""")),
            _ => { });

        (await client.GetAsync("o", "n")).Name.ShouldBe("n");
    }

    [Fact]
    public void A_nonsensical_budget_is_refused()
        => Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            var services = new ServiceCollection();
            services.AddRunaxHttp().UseSystemTextJson();
            services.AddRunaxClient<IRepositoryClient>().WithTotalTimeout(TimeSpan.Zero);
        });
}
