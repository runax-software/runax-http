namespace Runax.Http.Pipeline;

/// <summary>How a call's identity is propagated to the server.</summary>
public sealed class CorrelationOptions
{
    /// <summary>The header carrying the correlation id.</summary>
    public string HeaderName { get; set; } = CorrelationMiddleware.DefaultHeaderName;

    /// <summary>
    /// Whether to send W3C <c>traceparent</c> (and <c>tracestate</c>) from the ambient
    /// <see cref="System.Diagnostics.Activity"/> when there is one.
    /// </summary>
    /// <remarks>
    /// On, because it is what every tracing backend already understands, and because an
    /// <c>Activity</c> is usually already running — ASP.NET Core starts one per inbound request —
    /// so the outgoing call joins the existing trace rather than starting an orphan.
    /// </remarks>
    public bool PropagateTraceContext { get; set; } = true;

    /// <summary>
    /// Whether to overwrite a correlation header the caller already set.
    /// </summary>
    /// <remarks>
    /// Off. A header set explicitly on the method or at registration was set on purpose, and a
    /// stage that overrode it would make the explicit value the one thing that cannot be relied on.
    /// </remarks>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Produces the id when there is no ambient trace to take one from.
    /// </summary>
    /// <remarks>
    /// Defaults to a hex-encoded 128-bit value, matching the shape of a W3C trace id so the two
    /// sources of an id look alike in a log. Replace it to match an id format your estate already
    /// uses.
    /// </remarks>
    public Func<string> GenerateId { get; set; } = CorrelationMiddleware.NewTraceId;
}
