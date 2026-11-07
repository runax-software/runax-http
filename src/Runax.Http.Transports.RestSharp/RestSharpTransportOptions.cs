namespace Runax.Http.Transports;

/// <summary>How the RestSharp client behind the transport is built.</summary>
public sealed class RestSharpTransportOptions
{
    /// <summary>
    /// The name the transport is selected by. Change it to register more than one — a client per
    /// proxy, say, which is the usual reason.
    /// </summary>
    public string Name { get; set; } = RestSharpTransport.TransportName;

    /// <summary>
    /// A proxy for every request through this transport. RestSharp takes a proxy per client rather
    /// than per request, which is why this is here and why the transport does not declare
    /// <see cref="TransportCapabilities.PerRequestProxy"/>.
    /// </summary>
    public System.Net.IWebProxy? Proxy { get; set; }

    /// <summary>Whether to follow 3xx redirects. RestSharp's own default is <see langword="true"/>.</summary>
    public bool FollowRedirects { get; set; } = true;

    /// <summary>
    /// Which compression schemes to accept and decompress. Off by default, matching
    /// <c>HttpClient</c>'s behaviour; the transport declares
    /// <see cref="TransportCapabilities.AutomaticDecompression"/> only when this is set.
    /// </summary>
    public System.Net.DecompressionMethods AutomaticDecompression { get; set; }
        = System.Net.DecompressionMethods.None;

    /// <summary>
    /// A cookie container shared by every request through this transport. The transport declares
    /// <see cref="TransportCapabilities.Cookies"/> only when one is set.
    /// </summary>
    public System.Net.CookieContainer? CookieContainer { get; set; }

    /// <summary>
    /// The default timeout, used for requests that do not carry one of their own.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Applied to the finished options, for anything not surfaced here.</summary>
    public Action<RestSharp.RestClientOptions>? ConfigureClient { get; set; }
}
