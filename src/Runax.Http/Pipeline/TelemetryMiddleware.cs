using System.Diagnostics;
using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>
/// Records a span and a duration measurement for each request attempt, under the OpenTelemetry
/// HTTP client semantic conventions.
/// </summary>
/// <remarks>
/// <para>
/// One span per <em>attempt</em>, not per call. That is what the conventions describe — a resend
/// carries <c>http.request.resend_count</c> — and it is the only shape that can answer the question
/// retries create: whether a call was slow because the server was slow, or because the first two
/// tries failed and the backoff was waited out. A single span covering the lot reports the total and
/// hides which it was.
/// </para>
/// <para>
/// Outermost stage, before correlation. Two reasons, both about the stage below it: the span has to
/// enclose the time the other stages spend — an auth stage fetching a token off a cold cache is part
/// of how long the call took — and the correlation stage derives its id from
/// <see cref="Activity">Activity.Current</see>, which means the span has to exist before correlation
/// runs for the <c>traceparent</c> the server receives to name the span the client actually
/// recorded. Start the span later and the server's parent id points at nothing.
/// </para>
/// <para>
/// Nothing is recorded when nothing is listening: <see cref="ActivitySource.StartActivity(string,
/// ActivityKind)"/> returns <see langword="null"/> with no registered listener, and the histogram
/// is skipped when the meter is unsubscribed. An unobserved client pays for a null check.
/// </para>
/// </remarks>
public sealed class TelemetryMiddleware : IHttpMiddleware
{
    private readonly TelemetryOptions _options;
    private readonly string? _clientName;

    /// <summary>Creates the stage.</summary>
    /// <param name="options">What to record. Defaults to tracing and metrics both on.</param>
    /// <param name="clientName">
    /// The contract this stage belongs to, tagged as <c>runax.http.client</c>. Supplied by
    /// <c>UseTelemetry()</c>, which knows the contract type; <see langword="null"/> omits the tag.
    /// </param>
    public TelemetryMiddleware(TelemetryOptions? options = null, string? clientName = null)
    {
        _options = options ?? new TelemetryOptions();
        _clientName = clientName;
    }

    /// <inheritdoc />
    public int Order => HttpMiddlewareOrder.Telemetry;

    /// <inheritdoc />
    public async Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var method = context.Method.ToString().ToUpperInvariant();
        var template = Template(context);

        using var activity = _options.Tracing
            ? RunaxHttpTelemetry.ActivitySource.StartActivity(
                SpanName(method, template),
                ActivityKind.Client)
            : null;

        // Hoisted out of the branches below so the measurement and the span agree on what they are
        // describing even when one of the two is disabled.
        var measure = _options.Metrics && RunaxHttpTelemetry.RequestDuration.Enabled;

        if (activity is not null)
            Describe(activity, context, method, template);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await continuation(context, cancellationToken).ConfigureAwait(false);

            Complete(activity, context, response.StatusCode);

            if (measure)
                Record(context, method, template, started, response.StatusCode, errorType: null);

            return response;
        }
        catch (Exception exception)
        {
            // A cancelled call is still a measurement — a p99 that drops because slow calls are
            // being cancelled rather than completing is a graph that lies by omission. It is
            // labelled by its exception type like any other failure, which keeps it separable.
            var errorType = exception.GetType().FullName;

            Fail(activity, context, exception, errorType);

            if (measure)
                Record(context, method, template, started, statusCode: null, errorType);

            throw;
        }
    }

    /// <summary>
    /// The path template, which is the low-cardinality name for the endpoint.
    /// </summary>
    /// <remarks>
    /// Taken from the contract rather than parsed back out of the URL, because the URL has the
    /// placeholders already filled in and no reliable way to tell an id from a path segment.
    /// Relative to the base address: a base address with a path prefix of its own does not appear
    /// here, which keeps the value stable when the same contract is pointed at a different host.
    /// </remarks>
    private static string? Template(RequestContext context)
    {
        var template = context.ClientRequest.Template;

        return string.IsNullOrEmpty(template) ? null : template;
    }

    /// <summary>
    /// <c>GET /things/{id}</c>, or just <c>GET</c> with no template.
    /// </summary>
    /// <remarks>
    /// The conventions are emphatic that a span name must be low cardinality, which rules out the
    /// URL: a name containing an id produces one distinct operation per id, and a backend that
    /// groups by name ends up with a million groups of one.
    /// </remarks>
    private static string SpanName(string method, string? template)
        => template is null ? method : $"{method} {template}";

    private void Describe(Activity activity, RequestContext context, string method, string? template)
    {
        var uri = context.RequestUri;

        activity.SetTag("http.request.method", method);
        activity.SetTag("url.full", Describe(uri, _options.RecordQueryString));

        if (uri.IsAbsoluteUri)
        {
            activity.SetTag("url.scheme", uri.Scheme);
            activity.SetTag("server.address", uri.Host);

            // Only when it is not the default for the scheme. Tagging 443 on every https request
            // is a column of constants in every backend that stores it.
            if (!uri.IsDefaultPort)
                activity.SetTag("server.port", uri.Port);
        }

        if (template is not null)
            activity.SetTag("url.template", template);

        // Absent rather than zero on a first attempt, which is how the convention distinguishes
        // "this is the original request" from "this is a resend that happens to be numbered 0".
        if (context.Attempt > 1)
            activity.SetTag("http.request.resend_count", context.Attempt - 1);

        activity.SetTag("runax.http.transport", context.Transport.Name);

        if (_options.RecordClientName && _clientName is not null)
            activity.SetTag("runax.http.client", _clientName);
    }

    private static void Complete(Activity? activity, RequestContext context, int statusCode)
    {
        if (activity is null)
            return;

        activity.SetTag("http.response.status_code", statusCode);
        Correlate(activity, context);

        // 4xx and 5xx are both errors for a client span. The server may consider a 404 a perfectly
        // good answer; the caller that asked for the thing did not get it.
        if (statusCode >= 400)
        {
            activity.SetTag("error.type", statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            activity.SetStatus(ActivityStatusCode.Error);
        }
        else
        {
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Tags the correlation id, if a correlation stage produced one.
    /// </summary>
    /// <remarks>
    /// Set on the way out rather than on the way in, because the stage that assigns the id runs
    /// inside this one and there is nothing to read until it has. Usually the trace id in hex, in
    /// which case the tag is redundant with the span's own identity — but a client with
    /// <c>PropagateTraceContext</c> off, or its own <c>GenerateId</c>, sends something else
    /// entirely, and then this is the only thing joining the span to the logs and to whatever the
    /// server recorded under that header.
    /// </remarks>
    private static void Correlate(Activity activity, RequestContext context)
    {
        if (context.CorrelationId is { Length: > 0 } id)
            activity.SetTag("runax.http.correlation_id", id);
    }

    private static void Fail(
        Activity? activity,
        RequestContext context,
        Exception exception,
        string? errorType)
    {
        if (activity is null)
            return;

        activity.SetTag("error.type", errorType);
        Correlate(activity, context);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        // A transport failure may still have reached the server and come back with a status; an
        // HttpApiException certainly did. Recording it keeps a failed span comparable with a
        // successful one.
        if (exception is HttpApiException api)
            activity.SetTag("http.response.status_code", api.StatusCode);
    }

    private void Record(
        RequestContext context,
        string method,
        string? template,
        long started,
        int? statusCode,
        string? errorType)
    {
        var uri = context.RequestUri;

        // TagList rather than an array: it keeps up to eight tags inline, so a measurement on a hot
        // path does not allocate to describe itself.
        var tags = new TagList
        {
            { "http.request.method", method },
            { "runax.http.transport", context.Transport.Name }
        };

        if (uri.IsAbsoluteUri)
        {
            tags.Add("url.scheme", uri.Scheme);
            tags.Add("server.address", uri.Host);

            if (!uri.IsDefaultPort)
                tags.Add("server.port", uri.Port);
        }

        // url.full is deliberately absent. Every distinct id in a path would become its own time
        // series, and a metric backend charges for those by the million.
        if (template is not null)
            tags.Add("url.template", template);

        if (statusCode is { } status)
        {
            tags.Add("http.response.status_code", status);

            if (status >= 400)
                tags.Add("error.type", status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (errorType is not null)
            tags.Add("error.type", errorType);

        if (_options.RecordClientName && _clientName is not null)
            tags.Add("runax.http.client", _clientName);

        // Seconds, per the convention's unit. TotalSeconds off a Stopwatch timestamp, so the value
        // keeps sub-millisecond resolution rather than being rounded through an integer.
        RunaxHttpTelemetry.RequestDuration.Record(
            Stopwatch.GetElapsedTime(started).TotalSeconds,
            tags);
    }

    /// <summary>
    /// The URL as an attribute value, with any credentials in it removed.
    /// </summary>
    /// <remarks>
    /// Userinfo is stripped unconditionally — the conventions require it, and a password in a
    /// <c>url.full</c> attribute is a password in a tracing backend's search index. The query string
    /// is included by default, matching what the logging stage does with the same information;
    /// <see cref="TelemetryOptions.RecordQueryString"/> turns it off for an API that puts a key
    /// there.
    /// </remarks>
    private static string Describe(Uri uri, bool includeQuery)
    {
        if (!uri.IsAbsoluteUri)
            return uri.ToString();

        var components = UriComponents.Scheme | UriComponents.Host | UriComponents.Port
                         | UriComponents.Path;

        if (includeQuery)
            components |= UriComponents.Query;

        return uri.GetComponents(components, UriFormat.UriEscaped);
    }
}
