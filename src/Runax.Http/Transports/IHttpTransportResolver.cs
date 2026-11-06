namespace Runax.Http.Transports;

/// <summary>Finds a registered transport by name.</summary>
public interface IHttpTransportResolver
{
    /// <summary>The names of every registered transport.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>Resolves a transport by name, or the default when <paramref name="name"/> is null.</summary>
    /// <param name="name">The transport name, or <see langword="null"/> for the default.</param>
    /// <returns>The transport.</returns>
    /// <exception cref="UnknownTransportException">
    /// No transport is registered under that name, or no name was given and there is no default.
    /// </exception>
    IHttpTransport Resolve(string? name);

    /// <summary>Resolves a transport by name without throwing.</summary>
    /// <param name="name">The transport name.</param>
    /// <param name="transport">The transport, if one is registered under that name.</param>
    /// <returns><see langword="true"/> if a transport was found.</returns>
    bool TryResolve(string name, out IHttpTransport? transport);
}
