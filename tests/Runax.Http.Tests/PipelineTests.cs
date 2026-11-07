using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Pipeline;
using Runax.Http.Retry;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// The middleware pipeline: ordering, where it sits relative to retries, and what each built-in
/// stage does.
/// </summary>
/// <remarks>
/// The pipeline is the transport-agnostic replacement for <c>DelegatingHandler</c>, so every test
/// here runs through the ordinary client path rather than invoking a stage directly — a stage that
/// works in isolation but is never reached would pass either way.
/// </remarks>
public class PipelineTests
{
    private const string Body = """{"name":"n","stars":0}""";

    private static (IRepositoryClient Client, RecordingTransport Transport) Build(
        Action<IRunaxClientBuilder<IRepositoryClient>> configure,
        Func<TransportRequest, TransportResponse>? respond = null)
    {
        var transport = new RecordingTransport(
            respond: respond ?? (_ => Responses.Json(Body)));

        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        var client = services.AddRunaxClient<IRepositoryClient>();
        configure(client);

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IRepositoryClient>(), transport);
    }

    // ---------------------------------------------------------------- ordering

    [Fact]
    public async Task Stages_run_in_Order_not_registration_order()
    {
        // Registered backwards on purpose. If registration order leaked through, moving a Use…()
        // call up or down a fluent chain would silently change behaviour.
        var log = new List<string>();

        var (client, _) = Build(builder => builder
            .Use(new RecordingMiddleware(log, "outer", order: 100))
            .Use(new RecordingMiddleware(log, "inner", order: 300))
            .Use(new RecordingMiddleware(log, "middle", order: 200)));

        await client.GetAsync("o", "n");

        log.ShouldBe([
            "outer:enter", "middle:enter", "inner:enter",
            "inner:exit", "middle:exit", "outer:exit"
        ]);
    }

    [Fact]
    public async Task Equal_Order_keeps_registration_order()
    {
        // The documented tiebreak, which holds because the sort is stable.
        var log = new List<string>();

        var (client, _) = Build(builder => builder
            .Use(new RecordingMiddleware(log, "first", order: 100))
            .Use(new RecordingMiddleware(log, "second", order: 100)));

        await client.GetAsync("o", "n");

        log.ShouldBe(["first:enter", "second:enter", "second:exit", "first:exit"]);
    }

    [Fact]
    public void The_built_in_stages_are_ordered_correlation_logging_authentication()
    {
        // Correlation outermost so everything inside sees the id; authentication innermost so the
        // token is attached as late as possible.
        HttpMiddlewareOrder.Correlation.ShouldBeLessThan(HttpMiddlewareOrder.Logging);
        HttpMiddlewareOrder.Logging.ShouldBeLessThan(HttpMiddlewareOrder.Authentication);
    }

    [Fact]
    public async Task A_client_with_no_stages_still_works()
    {
        var (client, transport) = Build(_ => { });

        await client.GetAsync("o", "n");

        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_stage_can_short_circuit_without_reaching_the_transport()
    {
        var (client, transport) = Build(builder => builder.Use(new ShortCircuitMiddleware(200)));

        var repository = await client.GetAsync("o", "n");

        repository.Name.ShouldBe("short");
        transport.Requests.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- headers

    [Fact]
    public async Task A_header_a_stage_sets_reaches_the_transport()
    {
        var (client, transport) = Build(builder => builder
            .Use(new RecordingMiddleware(
                [], "tag", 100, context => context.SetHeader("X-Tag", "from-middleware"))));

        await client.GetAsync("o", "n");

        transport.Last.Headers
            .ShouldContain(header => header.Key == "X-Tag" && header.Value == "from-middleware");
    }

    [Fact]
    public async Task A_stage_sees_the_headers_the_client_already_built()
    {
        string? accept = null;

        var (client, _) = Build(builder => builder
            .Use(new RecordingMiddleware([], "peek", 100, context => accept = context.GetHeader("Accept"))));

        await client.GetAsync("o", "n");

        // Proof the pipeline runs after header merging rather than instead of it.
        accept.ShouldBe("application/json");
    }

    // ------------------------------------------------- the retry interaction

    [Fact]
    public async Task Stages_run_once_per_attempt_rather_than_once_per_call()
    {
        var statuses = new Queue<int>([503, 503, 200]);
        var stage = new RecordingMiddleware([], "counted", 100);

        var (client, transport) = Build(
            builder => builder
                .Use(stage)
                .WithRetry(options =>
                {
                    options.MaxAttempts = 3;
                    options.Delay = TimeSpan.Zero;
                }),
            _ =>
            {
                var status = statuses.Dequeue();
                return status == 200 ? Responses.Json(Body) : Responses.Error(status);
            });

        await client.GetAsync("o", "n");

        transport.Requests.Count.ShouldBe(3);
        stage.Invocations.ShouldBe(3);
    }

    [Fact]
    public async Task The_attempt_number_is_visible_to_a_stage()
    {
        var seen = new List<int>();
        var statuses = new Queue<int>([503, 200]);

        var (client, _) = Build(
            builder => builder
                .Use(new RecordingMiddleware([], "attempts", 100, context => seen.Add(context.Attempt)))
                .WithRetry(options =>
                {
                    options.MaxAttempts = 3;
                    options.Delay = TimeSpan.Zero;
                }),
            _ =>
            {
                var status = statuses.Dequeue();
                return status == 200 ? Responses.Json(Body) : Responses.Error(status);
            });

        await client.GetAsync("o", "n");

        seen.ShouldBe([1, 2]);
    }

    // ---------------------------------------------------------- authentication

    [Fact]
    public async Task A_bearer_token_is_attached()
    {
        var (client, transport) = Build(builder => builder.UseBearerToken(() => "abc123"));

        await client.GetAsync("o", "n");

        transport.Last.Headers
            .ShouldContain(header => header.Key == "Authorization" && header.Value == "Bearer abc123");
    }

    [Fact]
    public async Task A_refreshed_token_is_used_on_a_retry()
    {
        // The reason the pipeline sits inside the retry loop. Outside it, the second attempt would
        // carry the token that had already been rejected.
        var tokens = new Queue<string>(["expired", "refreshed"]);
        var statuses = new Queue<int>([503, 200]);

        var (client, transport) = Build(
            builder => builder
                .UseBearerToken(() => tokens.Dequeue())
                .WithRetry(options =>
                {
                    options.MaxAttempts = 3;
                    options.Delay = TimeSpan.Zero;
                }),
            _ =>
            {
                var status = statuses.Dequeue();
                return status == 200 ? Responses.Json(Body) : Responses.Error(status);
            });

        await client.GetAsync("o", "n");

        transport.Requests.Count.ShouldBe(2);
        Header(transport.Requests[0], "Authorization").ShouldBe("Bearer expired");
        Header(transport.Requests[1], "Authorization").ShouldBe("Bearer refreshed");
    }

    [Fact]
    public async Task A_retried_request_does_not_carry_both_tokens()
    {
        // SetHeader replaces rather than appends, so the stale credential does not ride along.
        var tokens = new Queue<string>(["first", "second"]);
        var statuses = new Queue<int>([503, 200]);

        var (client, transport) = Build(
            builder => builder
                .UseBearerToken(() => tokens.Dequeue())
                .WithRetry(options =>
                {
                    options.MaxAttempts = 3;
                    options.Delay = TimeSpan.Zero;
                }),
            _ =>
            {
                var status = statuses.Dequeue();
                return status == 200 ? Responses.Json(Body) : Responses.Error(status);
            });

        await client.GetAsync("o", "n");

        transport.Requests[1].Headers
            .Count(header => header.Key == "Authorization")
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_null_token_sends_the_request_unauthenticated()
    {
        var (client, transport) = Build(builder => builder.UseBearerToken(() => null));

        await client.GetAsync("o", "n");

        Header(transport.Last, "Authorization").ShouldBeNull();
    }

    [Fact]
    public async Task A_scheme_free_header_carries_the_value_alone()
    {
        // For an X-Api-Key-style header, where "Bearer " in front would be wrong.
        var (client, transport) = Build(builder => builder
            .UseBearerToken(new DelegateAccessTokenProvider(() => "key"), "X-Api-Key", scheme: null));

        await client.GetAsync("o", "n");

        Header(transport.Last, "X-Api-Key").ShouldBe("key");
    }

    [Fact]
    public async Task An_async_token_provider_is_awaited()
    {
        var (client, transport) = Build(builder => builder.UseBearerToken(async cancellationToken =>
        {
            await Task.Yield();
            return "async-token";
        }));

        await client.GetAsync("o", "n");

        Header(transport.Last, "Authorization").ShouldBe("Bearer async-token");
    }

    [Fact]
    public async Task The_token_provider_sees_the_request()
    {
        // So a token can be scoped to the resource being called.
        Uri? seen = null;

        var (client, _) = Build(builder => builder.UseBearerToken(
            new DelegateAccessTokenProvider((context, _) =>
            {
                seen = context.RequestUri;
                return new ValueTask<string?>("t");
            })));

        await client.GetAsync("o", "n");

        seen!.AbsolutePath.ShouldBe("/v3/repos/o/n");
    }

    // ------------------------------------------------------------- correlation

    [Fact]
    public async Task A_correlation_id_is_sent()
    {
        var (client, transport) = Build(builder => builder.UseCorrelation());

        await client.GetAsync("o", "n");

        Header(transport.Last, "X-Correlation-ID").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_correlation_id_is_the_same_on_every_attempt()
    {
        // The mirror image of the token: an id that changed between attempts could not correlate
        // the retries of one call, which is the only thing it is for.
        var statuses = new Queue<int>([503, 200]);

        var (client, transport) = Build(
            builder => builder
                .UseCorrelation()
                .WithRetry(options =>
                {
                    options.MaxAttempts = 3;
                    options.Delay = TimeSpan.Zero;
                }),
            _ =>
            {
                var status = statuses.Dequeue();
                return status == 200 ? Responses.Json(Body) : Responses.Error(status);
            });

        await client.GetAsync("o", "n");

        transport.Requests.Count.ShouldBe(2);

        var first = Header(transport.Requests[0], "X-Correlation-ID");
        first.ShouldNotBeNullOrWhiteSpace();
        Header(transport.Requests[1], "X-Correlation-ID").ShouldBe(first);
    }

    [Fact]
    public async Task A_header_the_caller_set_is_not_overwritten()
    {
        var (client, transport) = Build(builder => builder
            .WithDefaultHeader("X-Correlation-ID", "caller-supplied")
            .UseCorrelation());

        await client.GetAsync("o", "n");

        Header(transport.Last, "X-Correlation-ID").ShouldBe("caller-supplied");
    }

    [Fact]
    public async Task Overwrite_replaces_a_caller_supplied_header()
    {
        var (client, transport) = Build(builder => builder
            .WithDefaultHeader("X-Correlation-ID", "caller-supplied")
            .UseCorrelation(options => options.Overwrite = true));

        await client.GetAsync("o", "n");

        Header(transport.Last, "X-Correlation-ID").ShouldNotBe("caller-supplied");
    }

    [Fact]
    public async Task The_header_name_is_configurable()
    {
        var (client, transport) = Build(builder => builder
            .UseCorrelation(options => options.HeaderName = "X-Request-Id"));

        await client.GetAsync("o", "n");

        Header(transport.Last, "X-Request-Id").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_ambient_activity_supplies_the_id_and_traceparent()
    {
        // ASP.NET Core starts an Activity per inbound request, so the outgoing call should join the
        // trace already running rather than starting an unconnected one.
        using var source = new System.Diagnostics.ActivitySource("Runax.Http.Tests");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _)
                => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };

        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("outbound");
        activity.ShouldNotBeNull();

        var (client, transport) = Build(builder => builder.UseCorrelation());

        await client.GetAsync("o", "n");

        Header(transport.Last, "X-Correlation-ID").ShouldBe(activity!.TraceId.ToHexString());
        Header(transport.Last, "traceparent").ShouldBe(activity.Id);
    }

    [Fact]
    public async Task Trace_context_propagation_can_be_turned_off()
    {
        using var source = new System.Diagnostics.ActivitySource("Runax.Http.Tests.Off");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _)
                => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };

        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("outbound");

        var (client, transport) = Build(builder => builder
            .UseCorrelation(options => options.PropagateTraceContext = false));

        await client.GetAsync("o", "n");

        Header(transport.Last, "traceparent").ShouldBeNull();
    }

    private static string? Header(TransportRequest request, string name)
    {
        foreach (var (key, value) in request.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }
}
