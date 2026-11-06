using Runax.Http.Requests;

namespace Runax.Http;

/// <summary>
/// Runs a <see cref="ClientRequest"/>: resolves it into a transport-neutral request, picks a
/// transport, sends it, and turns the response into whatever the client method declared.
/// </summary>
/// <remarks>
/// <para>
/// Everything a client method needs beyond stating its own intent happens here, which is why
/// generated methods are three or four lines each. The logic is in a normal library that can be
/// read, debugged and unit-tested, rather than emitted into every consumer's assembly.
/// </para>
/// <para>
/// A transport is chosen by the first of these that produces one:
/// </para>
/// <list type="number">
/// <item><c>TransportScope.Use(...)</c> — an ambient per-call selection.</item>
/// <item><c>UseTransport(...)</c> — what the client was registered with.</item>
/// <item><see cref="TransportAttribute"/> — what the interface or method declares.</item>
/// <item>The resolver's default, which is the only transport when just one is registered.</item>
/// </list>
/// <para>
/// Per-call beats registration beats source, so the person closest to the decision wins. The
/// attribute is a default the author suggests, not a constraint on whoever consumes the interface.
/// </para>
/// </remarks>
public interface IHttpContractExecutor
{
    /// <summary>
    /// The serializer this client's bodies go through. Generated code calls it with the body
    /// parameter's static type, which is what keeps serialization reflection-free.
    /// </summary>
    Runax.Http.Serialization.IContentSerializer Serializer { get; }

    /// <summary>Sends a request whose response body is not wanted.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the response arrives.</returns>
    /// <exception cref="HttpApiException">The response carried a non-success status code.</exception>
    /// <exception cref="TransportException">The request did not complete.</exception>
    Task SendAsync(ClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a request and deserializes the response body.</summary>
    /// <typeparam name="T">The type to deserialize the body into.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The deserialized body, or <see langword="null"/> if there was none.</returns>
    /// <exception cref="HttpApiException">The response carried a non-success status code.</exception>
    /// <exception cref="TransportException">The request did not complete.</exception>
    Task<T?> SendAsync<T>(ClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and returns the outcome as a value, without throwing on an error status.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response, successful or not.</returns>
    /// <exception cref="TransportException">
    /// The request did not complete. A transport failure is still thrown — there was no response
    /// to describe.
    /// </exception>
    Task<ApiResponse> SendForApiResponseAsync(ClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and returns the outcome, with any body deserialized, as a value rather than
    /// throwing on an error status.
    /// </summary>
    /// <typeparam name="T">The type to deserialize a successful body into.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response, successful or not.</returns>
    /// <exception cref="TransportException">The request did not complete.</exception>
    Task<ApiResponse<T>> SendForApiResponseAsync<T>(ClientRequest request, CancellationToken cancellationToken = default);
}
