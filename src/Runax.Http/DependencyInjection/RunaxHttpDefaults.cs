namespace Runax.Http.DependencyInjection;

/// <summary>Application-wide Runax.Http settings that are not per client.</summary>
internal sealed class RunaxHttpDefaults
{
    /// <summary>
    /// The transport used by clients and requests that name none. <see langword="null"/> leaves it
    /// to the resolver, which treats a lone registered transport as the default and refuses to
    /// guess between several.
    /// </summary>
    internal string? DefaultTransportName { get; set; }

    /// <summary>
    /// Every registered client, so the transport names they ask for can be checked against the
    /// transports that exist.
    /// </summary>
    /// <remarks>
    /// Collected rather than validated at registration because a fluent chain runs as several
    /// statements: <c>UseTransport(...)</c> is called after <c>AddRunaxClient</c> has returned, and
    /// a transport package may well be registered after that. The only point at which the full
    /// picture exists is when the resolver is built.
    /// </remarks>
    internal List<RegisteredClient> Clients { get; } = [];
}

/// <summary>One registered client, for the sake of validating what it asks for.</summary>
/// <param name="Contract">The client interface, named in any error so it is obvious which one is wrong.</param>
/// <param name="Options">The client's options, read late so <c>UseTransport(...)</c> is included.</param>
/// <param name="DeclaredTransportNames">
/// Names from <see cref="TransportAttribute"/> on the interface or its methods, as the generator
/// found them.
/// </param>
internal sealed record RegisteredClient(
    Type Contract,
    RunaxClientOptions Options,
    IReadOnlyList<string> DeclaredTransportNames);
