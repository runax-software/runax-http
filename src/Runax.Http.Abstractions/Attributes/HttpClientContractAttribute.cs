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
