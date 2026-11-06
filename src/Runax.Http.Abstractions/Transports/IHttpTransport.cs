namespace Runax.Http.Transports;

/// <summary>
/// Sends an HTTP request. The boundary between Runax.Http and whichever HTTP library is doing
/// the work, and the whole reason one client interface can run over several of them.
/// </summary>
/// <remarks>
/// <para>
/// An implementation does one thing: translate a <see cref="TransportRequest"/> into its
/// library's own request, send it, and translate the result into a
/// <see cref="TransportResponse"/>. That's the entire job.
/// </para>
/// <para>
/// It specifically does <em>not</em> serialize bodies, deserialize responses, retry, log,
/// attach credentials, follow a redirect policy of its own, or throw on a non-success status.
/// All of that lives above this interface, written once, and works the same whichever transport
/// is underneath. A transport that takes on any of it makes that behaviour vary by transport,
/// which is the one thing this design exists to prevent.
/// </para>
/// <para>
/// Implementations must be thread-safe: a single instance is typically registered as a
/// singleton and used concurrently.
/// </para>
/// </remarks>
public interface IHttpTransport
{
    /// <summary>
    /// The name this transport is selected by — <c>httpclient</c>, <c>restsharp</c>, and so on.
    /// Compared case-insensitively, and unique across the transports registered together.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// What this transport can do, declared honestly. See <see cref="TransportCapabilities"/>
    /// for why claiming something you cannot do is worse than admitting it.
    /// </summary>
    TransportCapabilities Capabilities { get; }

    /// <summary>Sends the request.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">
    /// Cancels the request. Honoured mid-flight only if this transport declares
    /// <see cref="TransportCapabilities.CancellationMidFlight"/>.
    /// </param>
    /// <returns>
    /// The response, whatever its status code. A 404 or a 500 is a response, not a failure, and
    /// must be returned rather than thrown — deciding what a status code means belongs to the
    /// caller, which can see the client interface's declared return type.
    /// </returns>
    /// <exception cref="Runax.Http.TransportException">
    /// The request did not complete: DNS failure, connection refused, TLS failure, timeout. In
    /// other words, no status code was ever received.
    /// </exception>
    /// <exception cref="Runax.Http.TransportCapabilityException">
    /// The request needs a capability this transport does not have and does not degrade for.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was signalled.
    /// </exception>
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default);
}
