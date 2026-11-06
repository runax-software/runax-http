namespace Runax.Http.DependencyInjection;

/// <summary>
/// Everything the runtime needs to stand up one generated client: how to construct it, and the
/// defaults its interface declared.
/// </summary>
/// <remarks>
/// The declared defaults are carried here rather than read back off the interface at startup. The
/// generator has already seen <see cref="BaseAddressAttribute"/>, <see cref="TransportAttribute"/>
/// and the interface-level <see cref="HeaderAttribute"/>s at compile time, so emitting what it
/// found costs nothing and keeps registration free of attribute reflection — which matters under
/// trimming, where an attribute the application never otherwise mentions is exactly the kind of
/// thing that gets removed.
/// </remarks>
public sealed class GeneratedClient
{
    /// <summary>The client interface this describes.</summary>
    public required Type Contract { get; init; }

    /// <summary>Creates the generated implementation around an executor.</summary>
    public required Func<IHttpContractExecutor, object> Factory { get; init; }

    /// <summary>The address from <see cref="BaseAddressAttribute"/>, if the interface declared one.</summary>
    public string? BaseAddress { get; init; }

    /// <summary>The transport from <see cref="TransportAttribute"/>, if the interface declared one.</summary>
    public string? TransportName { get; init; }

    /// <summary>Constant headers from interface-level <see cref="HeaderAttribute"/>s.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> DefaultHeaders { get; init; } = [];
}
