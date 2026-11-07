namespace Runax.Http.Pipeline;

/// <summary>What the telemetry stage records.</summary>
public sealed class TelemetryOptions
{
    /// <summary>Whether to start an <see cref="System.Diagnostics.Activity"/> per attempt.</summary>
    public bool Tracing { get; set; } = true;

    /// <summary>Whether to record the request duration histogram.</summary>
    public bool Metrics { get; set; } = true;

    /// <summary>
    /// Whether <c>url.full</c> carries the query string.
    /// </summary>
    /// <remarks>
    /// On, matching what the logging stage does with the same information, so one library does not
    /// take two positions on it. Turn it off for an API that puts a key or a token in the query —
    /// a trace is stored and searchable for as long as your retention allows, which makes it a
    /// worse place for a secret than a log line.
    /// </remarks>
    public bool RecordQueryString { get; set; } = true;

    /// <summary>
    /// Whether to tag the contract that issued the request.
    /// </summary>
    /// <remarks>
    /// On, under <c>runax.http.client</c>. Low cardinality by construction — there are as many
    /// values as you have client interfaces — and it is the tag that answers "which of my clients
    /// is slow", which `server.address` cannot when several clients share a host.
    /// </remarks>
    public bool RecordClientName { get; set; } = true;
}
