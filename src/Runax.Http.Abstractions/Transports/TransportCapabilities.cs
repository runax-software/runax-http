namespace Runax.Http.Transports;

/// <summary>
/// What a transport can do. Declared by <see cref="IHttpTransport.Capabilities"/> and checked
/// before a request that needs something is handed to a transport that doesn't have it.
/// </summary>
/// <remarks>
/// <para>
/// This enum exists because HTTP libraries do not agree. One streams request bodies, another
/// buffers them; one takes a proxy per request, another only per client; one cancels
/// mid-flight, another checks the token between steps. A library that hides those differences
/// is lying to you somewhere, so Runax.Http makes each one answerable.
/// </para>
/// <para>
/// A missing capability has exactly one of two outcomes, chosen per capability and documented
/// by the transport: throw <see cref="Runax.Http.TransportCapabilityException"/>, or degrade in
/// a stated way (buffering a stream, say). What it must never do is quietly do something else
/// and report success.
/// </para>
/// </remarks>
[Flags]
public enum TransportCapabilities
{
    /// <summary>Nothing beyond sending a buffered request and reading a buffered response.</summary>
    None = 0,

    /// <summary>
    /// Can send a <see cref="StreamedContent"/> body without buffering it first. Without this,
    /// large uploads are held in memory in full.
    /// </summary>
    RequestStreaming = 1 << 0,

    /// <summary>
    /// Can hand back a response body as a live stream, before it has been fully received.
    /// Without this, <see cref="TransportResponse.Content"/> is a fully buffered stream.
    /// </summary>
    ResponseStreaming = 1 << 1,

    /// <summary>Honours <see cref="TransportRequest.Proxy"/> per request rather than per client.</summary>
    PerRequestProxy = 1 << 2,

    /// <summary>Honours <see cref="TransportRequest.Timeout"/> per request rather than per client.</summary>
    PerRequestTimeout = 1 << 3,

    /// <summary>Can send <see cref="MultipartFormContent"/>.</summary>
    Multipart = 1 << 4,

    /// <summary>Decompresses gzip/deflate/brotli responses transparently.</summary>
    AutomaticDecompression = 1 << 5,

    /// <summary>Follows 3xx redirects, and reports the final address on the response.</summary>
    AutomaticRedirect = 1 << 6,

    /// <summary>Keeps cookies across requests made through the same transport instance.</summary>
    Cookies = 1 << 7,

    /// <summary>Can negotiate HTTP/2.</summary>
    Http2 = 1 << 8,

    /// <summary>Can negotiate HTTP/3.</summary>
    Http3 = 1 << 9,

    /// <summary>
    /// Aborts an in-flight request when the <see cref="CancellationToken"/> fires, rather than
    /// only observing it between operations. Synchronous HTTP libraries wrapped in
    /// <see cref="System.Threading.Tasks.Task"/> typically cannot.
    /// </summary>
    CancellationMidFlight = 1 << 10
}
