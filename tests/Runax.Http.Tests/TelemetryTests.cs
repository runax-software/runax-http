using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Pipeline;
using Runax.Http.Retry;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// The spans and measurements the telemetry stage emits.
/// </summary>
/// <remarks>
/// <para>
/// Asserted against the attribute names from the OpenTelemetry HTTP client semantic conventions
/// rather than names of our own choosing, because conformance to those names is the entire value of
/// the feature: a dashboard built for <c>HttpClient</c> keeps working, and the attributes a backend
/// knows how to group by are the ones it is told to expect. A near-miss — <c>http.method</c> for
/// <c>http.request.method</c> — is indistinguishable from no instrumentation to anything consuming
/// it, and would pass a test that only checked "a tag is present".
/// </para>
/// <para>
/// These run <see cref="Xunit.CollectionAttribute">serially</see> against the rest of the suite,
/// because <see cref="ActivitySource.AddActivityListener"/> is process-wide: a listener added here
/// sees spans from a client another test built, and the counts below are exact.
/// </para>
/// </remarks>
[Collection(nameof(TelemetryTests))]
[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
public class TelemetryTests
{
    private static (IRepositoryClient Client, RecordingTransport Transport) Build(
        Func<TransportRequest, TransportResponse>? respond = null,
        Action<IRunaxClientBuilder<IRepositoryClient>>? configure = null,
        Action<TelemetryOptions>? telemetry = null)
    {
        var transport = new RecordingTransport(
            respond: respond ?? (_ => Responses.Json("""{"name":"n","stars":1}""")));

        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        // One telemetry stage, configured here rather than by the caller adding a second one:
        // two stages would each record a span and every exact count below would be off by one.
        var client = services.AddRunaxClient<IRepositoryClient>();
        client.UseTelemetry(telemetry);
        configure?.Invoke(client);

        return (services.BuildServiceProvider().GetRequiredService<IRepositoryClient>(), transport);
    }

    private static object? Tag(Activity activity, string name) => activity.GetTagItem(name);

    /// <summary>The list endpoint needs an array; the default stub answers with an object.</summary>
    private static TransportResponse Issues(TransportRequest _) => Responses.Json("[]");

    [Fact]
    public async Task A_span_is_named_by_method_and_template_not_by_url()
    {
        // The cardinality rule. "GET /repos/{owner}/{name}" groups every call to the endpoint;
        // "GET /repos/runax/http" is one operation per repository, and a backend grouping by name
        // ends up with a million groups of one.
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build();

        await client.GetAsync("runax", "http");

        recorder.Activity.DisplayName.ShouldBe("GET /repos/{owner}/{name}");
        recorder.Activity.Kind.ShouldBe(ActivityKind.Client);
    }

    [Fact]
    public async Task The_conventional_attributes_are_spelled_the_conventional_way()
    {
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build();

        await client.GetAsync("runax", "http");

        var activity = recorder.Activity;

        Tag(activity, "http.request.method").ShouldBe("GET");
        Tag(activity, "url.full").ShouldBe("https://api.example.com/v3/repos/runax/http");
        Tag(activity, "url.scheme").ShouldBe("https");
        Tag(activity, "url.template").ShouldBe("/repos/{owner}/{name}");
        Tag(activity, "server.address").ShouldBe("api.example.com");
        Tag(activity, "http.response.status_code").ShouldBe(200);
        activity.Status.ShouldBe(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task A_default_port_is_not_tagged()
    {
        // server.port on every https request is a column of 443s in whatever stores it.
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build();

        await client.GetAsync("runax", "http");

        Tag(recorder.Activity, "server.port").ShouldBeNull();
    }

    [Fact]
    public async Task The_active_transport_is_a_tag()
    {
        // The roadmap item's specific ask, and the question the tag exists to answer: whether a
        // latency change followed a transport swap. Above the IHttpTransport boundary, so it is the
        // same tag whichever transport answers.
        using var recorder = new TelemetryRecorder();
        var (client, transport) = Build();

        await client.GetAsync("runax", "http");

        Tag(recorder.Activity, "runax.http.transport").ShouldBe(transport.Name);
        recorder.Duration.Tags["runax.http.transport"].ShouldBe(transport.Name);
    }

    [Fact]
    public async Task The_contract_is_a_tag()
    {
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build();

        await client.GetAsync("runax", "http");

        Tag(recorder.Activity, "runax.http.client").ShouldBe(nameof(IRepositoryClient));
    }

    [Fact]
    public async Task A_four_hundred_is_an_error_on_a_client_span()
    {
        // The server may consider a 404 a fine answer. The caller that asked for the thing did not
        // get it, and the convention makes 4xx and 5xx errors for client spans on that basis.
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build(_ => Responses.Error(404));

        await Should.ThrowAsync<HttpApiException>(() => client.GetAsync("runax", "nope"));

        var activity = recorder.Activity;

        activity.Status.ShouldBe(ActivityStatusCode.Error);
        Tag(activity, "error.type").ShouldBe("404");
        Tag(activity, "http.response.status_code").ShouldBe(404);
    }

    [Fact]
    public async Task A_transport_failure_is_labelled_by_exception_type()
    {
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(_ => throw new InvalidOperationException("socket gone"));

        await Should.ThrowAsync<Exception>(() => client.GetAsync("runax", "http"));

        var activity = recorder.Activity;

        activity.Status.ShouldBe(ActivityStatusCode.Error);
        Tag(activity, "error.type").ShouldNotBeNull();
        Tag(activity, "http.response.status_code").ShouldBeNull();
    }

    [Fact]
    public async Task Each_attempt_gets_its_own_span_and_a_resend_is_numbered()
    {
        // One span per attempt is what the convention describes, and the only shape that separates
        // "the server was slow" from "the first two tries failed and we waited out the backoff".
        // A single span covering the lot reports the total and hides which it was.
        using var recorder = new TelemetryRecorder();

        var call = 0;

        var (client, _) = Build(
            _ => call++ == 0 ? Responses.Error(503) : Responses.Json("""{"name":"n","stars":1}"""),
            c => c.WithRetry(options =>
            {
                options.MaxAttempts = 2;
                options.Delay = TimeSpan.Zero;
                options.UseJitter = false;
            }));

        await client.GetAsync("runax", "http");

        var activities = recorder.Activities;

        activities.Count.ShouldBe(2);

        // Absent on the first, not zero: that is how the convention distinguishes an original
        // request from a resend that happens to be numbered 0.
        Tag(activities[0], "http.request.resend_count").ShouldBeNull();
        Tag(activities[1], "http.request.resend_count").ShouldBe(1);

        Tag(activities[0], "http.response.status_code").ShouldBe(503);
        Tag(activities[1], "http.response.status_code").ShouldBe(200);

        recorder.Measurements.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_duration_is_recorded_in_seconds_under_the_conventional_name()
    {
        // Seconds because the convention says seconds. A histogram in milliseconds cannot be
        // compared with every other HTTP duration in the same backend, which is most of what a
        // duration is for.
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build();

        await client.GetAsync("runax", "http");

        var duration = recorder.Duration;

        duration.Instrument.ShouldBe("http.client.request.duration");
        duration.Unit.ShouldBe("s");
        duration.Value.ShouldBeGreaterThan(0);
        duration.Value.ShouldBeLessThan(60);
    }

    [Fact]
    public async Task The_measurement_carries_no_high_cardinality_tag()
    {
        // url.full on a metric is one time series per id in the path, and a metric backend bills
        // for those by the million. The span is where the full URL belongs; the histogram is not.
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build(Issues);

        await client.ListIssuesAsync("runax", "http", "open", 2);

        var tags = recorder.Duration.Tags;

        // Keys.ShouldNotContain rather than ShouldNotContainKey: Shouldly only resolves the
        // latter against IReadOnlyDictionary from net9.0 onwards, and this suite builds for net8.0.
        tags.Keys.ShouldNotContain("url.full");
        tags.Keys.ShouldNotContain("runax.http.correlation_id");
        tags["url.template"].ShouldBe("/repos/{owner}/{name}/issues");
        tags["http.request.method"].ShouldBe("GET");
        tags["http.response.status_code"].ShouldBe(200);
    }

    [Fact]
    public async Task A_failed_call_is_still_measured()
    {
        // A p99 that improves because slow calls started failing rather than completing is a graph
        // that lies by omission.
        using var recorder = new TelemetryRecorder();
        var (client, _) = Build(_ => Responses.Error(503));

        await Should.ThrowAsync<HttpApiException>(() => client.GetAsync("runax", "http"));

        recorder.Duration.Tags["error.type"].ShouldBe("503");
    }

    [Fact]
    public async Task The_query_string_can_be_kept_out_of_the_span()
    {
        // A trace is stored and searchable for as long as retention allows, which makes it a worse
        // place for an API key than a log line. Included by default to match what the logging stage
        // does with the same information; off is one option away.
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(Issues, telemetry: o => o.RecordQueryString = false);

        await client.ListIssuesAsync("runax", "http", "open", 2);

        Tag(recorder.Activity, "url.full")
            .ShouldBe("https://api.example.com/v3/repos/runax/http/issues");
    }

    [Fact]
    public async Task The_query_string_is_recorded_by_default()
    {
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(Issues);

        await client.ListIssuesAsync("runax", "http", "open", 2);

        var url = (string?)Tag(recorder.Activity, "url.full");

        url.ShouldNotBeNull();
        url.ShouldContain("state=open");
    }

    [Fact]
    public async Task Credentials_in_the_url_never_reach_a_span()
    {
        // Unconditional, not an option. The convention requires it, and a password in a url.full
        // attribute is a password in a tracing backend's search index.
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(configure: c => c.Configure(options =>
            options.BaseAddress = new Uri("https://user:hunter2@api.example.com/v3/")));

        await client.GetAsync("runax", "http");

        var url = (string?)Tag(recorder.Activity, "url.full");

        url.ShouldNotBeNull();
        url.ShouldNotContain("hunter2");
        url.ShouldStartWith("https://api.example.com/");
    }

    [Fact]
    public async Task The_span_exists_before_correlation_runs_so_traceparent_names_it()
    {
        // The ordering constraint, and the reason the telemetry stage sits outside correlation
        // rather than beside it. Correlation derives its id from Activity.Current; start the span
        // after it and the traceparent reaching the server names a span that does not exist, which
        // breaks the join between the client trace and the server's — silently, since both halves
        // look fine on their own.
        using var recorder = new TelemetryRecorder();

        var (client, transport) = Build(configure: c => c.UseCorrelation());

        await client.GetAsync("runax", "http");

        var traceparent = transport.Last.Headers
            .First(h => string.Equals(h.Key, "traceparent", StringComparison.OrdinalIgnoreCase))
            .Value;

        traceparent.ShouldContain(recorder.Activity.TraceId.ToHexString());
    }

    [Fact]
    public async Task The_correlation_id_is_tagged_once_the_stage_below_has_assigned_it()
    {
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(configure: c => c.UseCorrelation(o => o.PropagateTraceContext = false));

        await client.GetAsync("runax", "http");

        Tag(recorder.Activity, "runax.http.correlation_id").ShouldNotBeNull();
    }

    [Fact]
    public async Task Tracing_and_metrics_are_separately_disableable()
    {
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(telemetry: o => o.Tracing = false);

        await client.GetAsync("runax", "http");

        recorder.Activities.ShouldBeEmpty();
        recorder.Duration.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Metrics_can_be_off_with_tracing_left_on()
    {
        using var recorder = new TelemetryRecorder();

        var (client, _) = Build(telemetry: o => o.Metrics = false);

        await client.GetAsync("runax", "http");

        recorder.Measurements.ShouldBeEmpty();
        recorder.Activity.DisplayName.ShouldBe("GET /repos/{owner}/{name}");
    }

    [Fact]
    public async Task A_client_with_nothing_listening_still_works()
    {
        // No recorder in scope. StartActivity returns null with no listener and the histogram is
        // unsubscribed, so the stage has to do nothing without tripping over its own nulls — this
        // is the path almost every consumer is on.
        var (client, _) = Build();

        (await client.GetAsync("runax", "http")).Name.ShouldBe("n");
    }
}
