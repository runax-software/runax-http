namespace Runax.Http;

/// <summary>
/// Names the transport a client interface or method should use by default, matching
/// <see cref="Runax.Http.Transports.IHttpTransport.Name"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a default, not a binding: it loses to a transport chosen at registration, which in turn
/// loses to one selected per call. Overriding it at registration is how a consumer who does not
/// want the transport an interface asks for opts out.
/// </para>
/// <para>
/// Prefer the transport's own constant — <c>[Transport(HttpClientTransport.TransportName)]</c> —
/// over a literal. A name cannot be validated at compile time, because what is registered is a
/// run-time fact and may come from configuration; it is checked when transports are first
/// resolved, so a name nothing is registered under fails there rather than on whichever request
/// first asks for it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public sealed class TransportAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="TransportAttribute"/> class.</summary>
    /// <param name="name">The transport name, for example <c>httpclient</c> or <c>restsharp</c>.</param>
    public TransportAttribute(string name) => Name = name;

    /// <summary>The transport name.</summary>
    public string Name { get; }
}
