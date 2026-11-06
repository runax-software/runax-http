namespace Runax.Http;

/// <summary>
/// The request never completed, so there is no status code to interpret: DNS failure,
/// connection refused, TLS failure, timeout.
/// </summary>
/// <remarks>
/// Distinct from <see cref="HttpApiException"/> on purpose. "The server said 503" and "the
/// server was never reached" call for different handling — one is often worth retrying against
/// the same host, the other may mean the host is wrong — and every HTTP library signals the
/// difference in its own way. Transports normalize it to this.
/// </remarks>
public class TransportException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TransportException"/> class.</summary>
    public TransportException() : this("The HTTP request did not complete.") { }

    /// <summary>Initializes a new instance of the <see cref="TransportException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public TransportException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="TransportException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">
    /// The HTTP library's own exception, kept so the underlying detail isn't lost.
    /// </param>
    public TransportException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>The name of the transport that failed.</summary>
    public string? TransportName { get; init; }

    /// <summary>The address that was being requested.</summary>
    public Uri? RequestUri { get; init; }
}
