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
}
