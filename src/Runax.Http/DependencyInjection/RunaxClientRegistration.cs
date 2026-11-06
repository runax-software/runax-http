namespace Runax.Http.DependencyInjection;

/// <summary>
/// Holds one client's options in the container.
/// </summary>
/// <remarks>
/// A container entry of its own, rather than options captured in the client's factory closure, so
/// that configuration applied through the builder after <c>AddRunaxClient</c> returns is still
/// seen when the client is first resolved. A fluent chain reads as one statement but runs as
/// several, and the client may be resolved long after all of them.
/// </remarks>
/// <typeparam name="TContract">The client interface these options belong to.</typeparam>
internal sealed class RunaxClientRegistration<TContract>
    where TContract : class
{
    internal RunaxClientRegistration(RunaxClientOptions options) => Options = options;

    internal RunaxClientOptions Options { get; }
}
