using System.Diagnostics;
using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>
/// Gives each call an id, keeps it the same across retries, and sends it to the server.
/// </summary>
/// <remarks>
/// <para>
/// Outermost, so every stage inside — and every log line — sees the same id.
/// </para>
/// <para>
/// The id is assigned on the first attempt and left alone afterwards. That is deliberate and is the
/// mirror image of authentication: a token must be re-read per attempt, an id must not change
/// between them. Two retries of one logical call that arrive with different ids cannot be
/// correlated into one story, which is the only thing the id is for. The attempts are told apart by
/// <see cref="RequestContext.Attempt"/> instead.
/// </para>
/// <para>
/// When an <see cref="Activity"/> is running — ASP.NET Core starts one per inbound request — its
/// trace id is used and W3C <c>traceparent</c> is propagated, so the outgoing call joins the trace
/// already in progress rather than starting an unconnected one. Creating spans is a separate
/// concern and belongs with the observability work, not here.
/// </para>
/// </remarks>
public sealed class CorrelationMiddleware : IHttpMiddleware
{
    /// <summary>The header a correlation id is written to unless told otherwise.</summary>
    public const string DefaultHeaderName = "X-Correlation-ID";

    private readonly CorrelationOptions _options;

    /// <summary>Initializes a new instance of the <see cref="CorrelationMiddleware"/> class.</summary>
    /// <param name="options">How to propagate the id. Defaults are sensible.</param>
    public CorrelationMiddleware(CorrelationOptions? options = null)
        => _options = options ?? new CorrelationOptions();

    /// <inheritdoc />
    public int Order => HttpMiddlewareOrder.Correlation;

    /// <summary>A fresh id, shaped like a W3C trace id so both sources look alike in a log.</summary>
    /// <returns>A 32-character hexadecimal id.</returns>
    public static string NewTraceId() => ActivityTraceId.CreateRandom().ToHexString();

    /// <inheritdoc />
    public Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var activity = Activity.Current;

        // ??= is what makes the id stable: on a retry the context already carries one, so this
        // stage re-running does not mint a second.
        context.CorrelationId ??= activity?.TraceId.ToHexString() ?? _options.GenerateId();

        if (_options.Overwrite || context.GetHeader(_options.HeaderName) is null)
            context.SetHeader(_options.HeaderName, context.CorrelationId);

        if (_options.PropagateTraceContext && activity is { IdFormat: ActivityIdFormat.W3C })
        {
            // Read from the Activity rather than assembled by hand, so the sampling flag and the
            // parent span id are the real ones and the server continues the right trace.
            if (activity.Id is { Length: > 0 } traceparent
                && (_options.Overwrite || context.GetHeader("traceparent") is null))
            {
                context.SetHeader("traceparent", traceparent);
            }

            if (activity.TraceStateString is { Length: > 0 } tracestate
                && (_options.Overwrite || context.GetHeader("tracestate") is null))
            {
                context.SetHeader("tracestate", tracestate);
            }
        }

        return continuation(context, cancellationToken);
    }
}
