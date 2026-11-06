namespace Runax.Http;

/// <summary>
/// Marks an interface as a Runax.Http client, so the generator emits an implementation for it.
/// </summary>
/// <remarks>
/// Only needed when an interface has no <see cref="HttpMethodAttribute"/>-annotated members of
/// its own (for example one that only inherits them). Interfaces that do carry request
/// attributes are picked up without it.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class HttpClientContractAttribute : Attribute;

/// <summary>
/// Sets the default base address for every request on a client interface. Registration-time
/// configuration overrides it, so this is a convenience for fixed first-party APIs rather than
/// something a consumer is stuck with.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class BaseAddressAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="BaseAddressAttribute"/> class.</summary>
    /// <param name="baseAddress">An absolute URI that request paths are resolved against.</param>
    public BaseAddressAttribute(string baseAddress) => BaseAddress = baseAddress;

    /// <summary>An absolute URI that request paths are resolved against.</summary>
    public string BaseAddress { get; }
}

/// <summary>
/// Names the transport a client interface or method should use by default, matching
/// <see cref="Runax.Http.Transports.IHttpTransport.Name"/>.
/// </summary>
/// <remarks>
/// This is a default, not a binding: it loses to a transport chosen at registration, which in
/// turn loses to one selected per call. A name with no registered transport behind it is a
/// build error, not a runtime one.
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
