namespace Runax.Http.Transports;

/// <summary>
/// A proxy to route a single request through, independent of any proxy the transport was
/// constructed with.
/// </summary>
/// <remarks>
/// Requires <see cref="TransportCapabilities.PerRequestProxy"/>. Plenty of HTTP libraries only
/// take a proxy when the client is built, which makes per-request rotation impossible without a
/// client per proxy; a transport that works that way should say so through its capabilities
/// rather than silently sending the request direct.
/// </remarks>
public sealed class RequestProxy
{
    /// <summary>Initializes a new instance of the <see cref="RequestProxy"/> class.</summary>
    /// <param name="host">The proxy host name or address.</param>
    /// <param name="port">The proxy port.</param>
    /// <param name="protocol">The protocol the proxy speaks.</param>
    public RequestProxy(string host, int port, ProxyProtocol protocol = ProxyProtocol.Http)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        Host = host;
        Port = port;
        Protocol = protocol;
    }

    /// <summary>The proxy host name or address.</summary>
    public string Host { get; }

    /// <summary>The proxy port.</summary>
    public int Port { get; }

    /// <summary>The protocol the proxy speaks.</summary>
    public ProxyProtocol Protocol { get; }

    /// <summary>The user name for proxy authentication, if it needs any.</summary>
    public string? UserName { get; init; }

    /// <summary>The password for proxy authentication, if it needs any.</summary>
    public string? Password { get; init; }
}
