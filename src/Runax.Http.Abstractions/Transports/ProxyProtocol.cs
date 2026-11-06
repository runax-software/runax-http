namespace Runax.Http.Transports;

/// <summary>The protocol a <see cref="RequestProxy"/> speaks.</summary>
public enum ProxyProtocol
{
    /// <summary>HTTP proxy.</summary>
    Http,

    /// <summary>HTTPS proxy.</summary>
    Https,

    /// <summary>SOCKS4a proxy.</summary>
    Socks4,

    /// <summary>SOCKS5 proxy.</summary>
    Socks5
}
