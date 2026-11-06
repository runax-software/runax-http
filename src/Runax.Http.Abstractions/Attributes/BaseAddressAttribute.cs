namespace Runax.Http;

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
