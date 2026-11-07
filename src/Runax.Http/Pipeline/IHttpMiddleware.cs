using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>
/// A stage that wraps every request a client sends, above the transport boundary.
/// </summary>
/// <remarks>
/// <para>
/// This is the replacement for <c>DelegatingHandler</c>, which only exists in
/// <c>HttpClient</c>-land: anything built on it cannot be reused for RestSharp or any other
/// transport. Middleware here runs identically whichever transport is underneath, for the same
/// reason serialization, retries and the capability check live above
/// <see cref="IHttpTransport"/>.
/// </para>
/// <para>
/// <strong>A stage runs once per attempt, not once per call.</strong> A retried request passes
/// through the pipeline again, so anything that must be fresh on a retry — an access token, most
/// obviously — simply works. Anything that must <em>not</em> change between attempts belongs on the
/// <see cref="RequestContext"/>, which is created once per call and survives every attempt.
/// </para>
/// <para>
/// Implementations must be thread-safe: one instance serves every call on a client.
/// </para>
/// </remarks>
public interface IHttpMiddleware
{
    /// <summary>
    /// Where this stage sits. Lower runs first, so a lower number is further from the wire.
    /// </summary>
    /// <remarks>
    /// Explicit rather than taken from registration order, so moving a <c>Use…()</c> call up or
    /// down a fluent chain cannot silently change behaviour. See <see cref="HttpMiddlewareOrder"/>
    /// for the built-in positions. Equal values keep the order they were registered in.
    /// </remarks>
    int Order { get; }

    /// <summary>Runs this stage, calling <paramref name="continuation"/> to continue.</summary>
    /// <param name="context">The request being sent. Headers may be modified; see the remarks.</param>
    /// <param name="continuation">The rest of the pipeline. Not calling it short-circuits the request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response.</returns>
    /// <remarks>
    /// Headers are the only part of the request a stage may change, which is a deliberate limit.
    /// <see cref="TransportRequest.RequiredCapabilities"/> is derived from the body, proxy, timeout
    /// and HTTP version, and the capability check runs before the pipeline — so letting a stage
    /// alter one of those would let it introduce a requirement nothing had checked.
    /// </remarks>
    Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken);
}
