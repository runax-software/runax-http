namespace Runax.Http.Retry;

/// <summary>
/// When and how a request is retried. Off until you turn it on.
/// </summary>
/// <remarks>
/// <para>
/// Retrying is off by default because a client that retries without being asked is a client that
/// can turn one slow dependency into an outage. Turning it on is <c>WithRetry(3)</c>; everything
/// below has a defensible default once it is on.
/// </para>
/// <para>
/// Two defaults are worth knowing about because they are safety decisions rather than taste:
/// non-idempotent methods are not retried, and a request whose body is a stream is not retried.
/// See <see cref="RetryNonIdempotentMethods"/> and the remarks on this type.
/// </para>
/// <para>
/// A request body that cannot be replayed is never retried, whatever this says. A
/// <see cref="Transports.StreamedContent"/> body has already been consumed by the first attempt,
/// so a second one would send a truncated request; buffering it instead would mean quietly holding
/// an upload of unknown size in memory. Neither is acceptable silently, so such a request is sent
/// once and its failure surfaces normally.
/// </para>
/// </remarks>
public sealed class RetryOptions
{
    /// <summary>
    /// Status codes worth retrying: request timeout, too many requests, and the 5xx codes that
    /// indicate the server could not answer rather than would not.
    /// </summary>
    /// <remarks>
    /// Notably absent: 500. A plain Internal Server Error is usually a bug in the request or the
    /// handler, and repeating it just multiplies the log noise. Add it if your server is known to
    /// use 500 for transient faults.
    /// </remarks>
    public static readonly int[] DefaultRetryableStatusCodes = [408, 429, 502, 503, 504];

    /// <summary>
    /// The methods that are safe to repeat. HTTP defines these as idempotent: sending one twice
    /// has the same effect as sending it once.
    /// </summary>
    public static readonly RequestMethod[] IdempotentMethods =
    [
        RequestMethod.Get,
        RequestMethod.Head,
        RequestMethod.Options,
        RequestMethod.Put,
        RequestMethod.Delete
    ];

    /// <summary>
    /// Total attempts, including the first. <c>1</c> means no retrying, and is the default.
    /// </summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>The wait before the second attempt, which <see cref="Backoff"/> grows from.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A ceiling on the computed wait, so exponential backoff over many attempts cannot turn into
    /// a wait nobody intended.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How the wait grows between attempts.</summary>
    public BackoffStrategy Backoff { get; set; } = BackoffStrategy.Exponential;

    /// <summary>
    /// Whether to spread the wait randomly over part of its range.
    /// </summary>
    /// <remarks>
    /// On by default. Without it, every client that failed at the same moment retries at the same
    /// moment, and a service recovering from a blip gets hit by a synchronized wave — the problem
    /// the retry was meant to ride out, rebuilt on a timer.
    /// </remarks>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Whether a <c>Retry-After</c> header overrides the computed wait.
    /// </summary>
    /// <remarks>
    /// On by default. When a server says how long to wait it knows something the client does not,
    /// and ignoring it on a 429 is how a rate limit becomes a ban.
    /// </remarks>
    public bool RespectRetryAfter { get; set; } = true;

    /// <summary>
    /// The longest <c>Retry-After</c> that will be honoured. A longer one is treated as a refusal
    /// to retry rather than as an instruction to block for it.
    /// </summary>
    public TimeSpan MaxRetryAfterDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether to retry methods that are not idempotent — <c>POST</c> and <c>PATCH</c>.
    /// </summary>
    /// <remarks>
    /// Off by default, and the most consequential default here. A retried <c>POST</c> that timed
    /// out after the server committed it creates the thing twice, and the client has no way to
    /// know which happened. Turn this on only for an endpoint with an idempotency key or a
    /// genuinely idempotent handler.
    /// </remarks>
    public bool RetryNonIdempotentMethods { get; set; }

    /// <summary>
    /// Status codes that trigger a retry. Defaults to <see cref="DefaultRetryableStatusCodes"/>.
    /// </summary>
    public ISet<int> RetryableStatusCodes { get; } = new HashSet<int>(DefaultRetryableStatusCodes);

    /// <summary>
    /// Whether a request that never completed — DNS failure, connection refused, TLS failure,
    /// timeout — is retried. On by default: this is the case retrying is actually for.
    /// </summary>
    public bool RetryTransportFailures { get; set; } = true;

    /// <summary>
    /// The clock used to wait between attempts. Replaceable so tests do not have to.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Whether retrying is on at all.</summary>
    internal bool IsEnabled => MaxAttempts > 1;

    /// <summary>Whether this method may be repeated under the current settings.</summary>
    internal bool AllowsMethod(RequestMethod method)
        => RetryNonIdempotentMethods || Array.IndexOf(IdempotentMethods, method) >= 0;
}
