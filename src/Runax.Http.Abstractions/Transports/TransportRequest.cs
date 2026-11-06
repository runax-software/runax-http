namespace Runax.Http.Transports;

/// <summary>
/// One HTTP request, described without reference to any HTTP library. This is the only thing a
/// transport is ever handed, and the reason the same client interface can run over
/// <c>HttpClient</c>, RestSharp or anything else.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequestUri"/> is absolute and final: the path template is already substituted and
/// the query string already built and encoded. Transports do not compose URLs. That is
/// deliberate — URL joining is where HTTP libraries disagree most (whether a leading slash on
/// the path replaces the base path, whether a trailing slash on the base address is kept,
/// how a query value is escaped), and those differences would otherwise show up as the same
/// client hitting different URLs depending on which transport was selected.
/// </para>
/// <para>
/// Likewise the body is already serialized. By the time a request gets here, everything that
/// could be decided once has been.
/// </para>
/// </remarks>
public sealed class TransportRequest
{
    /// <summary>The HTTP method to issue.</summary>
    public required RequestMethod Method { get; init; }

    /// <summary>
    /// The absolute target, with the query string already composed and encoded.
    /// </summary>
    public required Uri RequestUri { get; init; }

    /// <summary>
    /// The request headers. A list rather than a dictionary because repeating a header is legal
    /// and sometimes required, and because order is occasionally significant.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>The body, or <see langword="null"/> for a request without one.</summary>
    public RequestContent? Content { get; init; }

    /// <summary>
    /// How long this request may take, or <see langword="null"/> for the transport's own
    /// default. Requires <see cref="TransportCapabilities.PerRequestTimeout"/>.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// A proxy to route this request through, overriding whatever the transport was built with.
    /// Requires <see cref="TransportCapabilities.PerRequestProxy"/>.
    /// </summary>
    public RequestProxy? Proxy { get; init; }

    /// <summary>
    /// The HTTP version to prefer, or <see langword="null"/> for the transport's default.
    /// A transport that cannot negotiate the requested version says so through
    /// <see cref="TransportCapabilities.Http2"/> / <see cref="TransportCapabilities.Http3"/>.
    /// </summary>
    public Version? Version { get; init; }

    /// <summary>
    /// What this request needs from a transport beyond the baseline, derived from the request
    /// itself: a streamed body needs <see cref="TransportCapabilities.RequestStreaming"/>,
    /// a multipart body needs <see cref="TransportCapabilities.Multipart"/>, and so on.
    /// </summary>
    /// <remarks>
    /// This is what makes the capability check mechanical rather than a matter of each
    /// transport remembering to look. It covers only what is visible in the request; a
    /// requirement that comes from configuration (cookies across requests, say) is checked
    /// where that configuration is applied.
    /// </remarks>
    public TransportCapabilities RequiredCapabilities
    {
        get
        {
            var required = TransportCapabilities.None;

            if (Content is StreamedContent)
                required |= TransportCapabilities.RequestStreaming;

            if (Content is MultipartFormContent)
                required |= TransportCapabilities.Multipart;

            if (Proxy is not null)
                required |= TransportCapabilities.PerRequestProxy;

            if (Timeout is not null)
                required |= TransportCapabilities.PerRequestTimeout;

            if (Version is { Major: 2 })
                required |= TransportCapabilities.Http2;

            if (Version is { Major: 3 })
                required |= TransportCapabilities.Http3;

            return required;
        }
    }
}
