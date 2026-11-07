namespace Runax.Http;

/// <summary>
/// Retries this one method, for the flaky endpoint on an otherwise healthy API.
/// </summary>
/// <remarks>
/// <para>
/// Retrying is off by default, and <c>WithRetry(...)</c> at registration turns it on for a whole
/// client. This is the other granularity: one endpoint that needs it, declared by the person who
/// knows the endpoint.
/// </para>
/// <code>
/// [Get("/reports/{id}")]
/// [Retry(5, DelayMilliseconds = 250)]
/// Task&lt;Report&gt; GetReportAsync(int id, CancellationToken ct = default);
/// </code>
/// <para>
/// <strong>It refines the client's settings rather than replacing them.</strong> Whatever the
/// client was registered with — backoff curve, jitter, which status codes count,
/// <c>Retry-After</c> handling — still applies; this changes only what it names. And it wins over
/// the client's attempt count, because a method and a client are answering different-sized
/// questions: "how should this endpoint behave" refines "how should this client behave by
/// default", exactly as a per-request timeout refines the client's timeout and a method's
/// <see cref="HeaderAttribute"/> refines a client-wide default header.
/// </para>
/// <para>
/// (<see cref="TransportAttribute"/> resolves the other way round, with registration winning. That
/// is not an inconsistency: a transport is a <em>competing</em> claim about one fact — which stack
/// sends the request — so one of the two has to lose, and the operator knows the deployment. A
/// retry count is a refinement, so there is no tie to break.)
/// </para>
/// <para>
/// The operator keeps the last word. <c>WithoutRetries()</c> on a client turns retrying off and
/// ignores every <c>[Retry]</c> its methods declare — a client that cannot be stopped from
/// retrying is a liability in the middle of an incident.
/// </para>
/// <para>
/// The safety defaults still hold. A body that cannot be replayed is never retried, whatever this
/// says, because the first attempt already consumed the stream. A non-idempotent method is not
/// retried either unless <see cref="AllowNonIdempotent"/> says so — and a <c>[Retry]</c> on a
/// <c>POST</c> without it is a warning, because it would otherwise do nothing at all.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RetryAttribute : Attribute
{
    /// <summary>Means "leave this to the client's configuration".</summary>
    /// <remarks>
    /// A sentinel because attribute arguments have to be compile-time constants, so a nullable
    /// <see cref="int"/> is not available and <c>0</c> is a legitimate delay rather than an absent
    /// one.
    /// </remarks>
    public const int Inherit = -1;

    /// <summary>Initializes a new instance of the <see cref="RetryAttribute"/> class.</summary>
    /// <param name="maxAttempts">
    /// Total attempts including the first, so <c>3</c> means one try and two retries. <c>1</c> is
    /// meaningful: it says this method must not be retried even though its client is configured to
    /// retry, which is how you exempt one dangerous call from a client-wide policy.
    /// </param>
    public RetryAttribute(int maxAttempts) => MaxAttempts = maxAttempts;

    /// <summary>Total attempts including the first.</summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// The wait before the second attempt, in milliseconds, which the client's backoff curve grows
    /// from. <see cref="Inherit"/> by default, leaving it to the client.
    /// </summary>
    /// <remarks>
    /// Milliseconds rather than a <see cref="TimeSpan"/>, which cannot be an attribute argument.
    /// </remarks>
    public int DelayMilliseconds { get; set; } = Inherit;

    /// <summary>
    /// Whether this method may be retried even though its HTTP method is not idempotent.
    /// </summary>
    /// <remarks>
    /// Off by default, and worth the long name. A retried <c>POST</c> that timed out after the
    /// server committed it creates the thing twice, and the client cannot tell which happened — so
    /// this is only for an endpoint with an idempotency key or a genuinely idempotent handler.
    /// <para>
    /// Here rather than only at registration because the interface author is the one who knows
    /// whether a particular endpoint is safe to repeat. The client-wide
    /// <c>RetryNonIdempotentMethods</c> is the blunter instrument: it opts in every method at once.
    /// </para>
    /// </remarks>
    public bool AllowNonIdempotent { get; set; }
}
