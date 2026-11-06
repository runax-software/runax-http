using Runax.Http.Transports;

namespace Runax.Http;

/// <summary>
/// The request needs something the selected transport cannot do.
/// </summary>
/// <remarks>
/// Raised rather than silently doing something else. A client that asks for a per-request proxy
/// and gets a direct connection, or asks to stream a 2 GB upload and gets it buffered into
/// memory, has been failed quietly — which is worse than being failed loudly, because the
/// symptom shows up somewhere else entirely.
/// </remarks>
public sealed class TransportCapabilityException : TransportException
{
    /// <summary>Initializes a new instance of the <see cref="TransportCapabilityException"/> class.</summary>
    public TransportCapabilityException()
        : this("The request requires a capability the selected transport does not have.") { }

    /// <summary>Initializes a new instance of the <see cref="TransportCapabilityException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public TransportCapabilityException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="TransportCapabilityException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TransportCapabilityException(string message, Exception? innerException)
        : base(message, innerException) { }

    /// <summary>The capabilities the request needed and did not get.</summary>
    public TransportCapabilities Missing { get; init; }

    /// <summary>
    /// Creates an exception describing the gap between what a request needed and what a
    /// transport offers.
    /// </summary>
    /// <param name="transport">The transport that was selected.</param>
    /// <param name="required">The capabilities the request needs.</param>
    /// <param name="requestUri">The address being requested.</param>
    /// <returns>The exception, or <see langword="null"/> if the transport can do everything required.</returns>
    public static TransportCapabilityException? ForMissing(
        IHttpTransport transport,
        TransportCapabilities required,
        Uri? requestUri = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        var missing = required & ~transport.Capabilities;
        if (missing == TransportCapabilities.None)
            return null;

        return new TransportCapabilityException(
            $"Transport '{transport.Name}' does not support {missing}, which this request requires.")
        {
            Missing = missing,
            TransportName = transport.Name,
            RequestUri = requestUri
        };
    }
}
