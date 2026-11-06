namespace Runax.Http;

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
