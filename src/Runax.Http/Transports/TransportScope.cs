namespace Runax.Http.Transports;

/// <summary>
/// Selects a transport for everything done inside a scope, without changing any method signature.
/// </summary>
/// <remarks>
/// <para>
/// The highest-precedence way to choose a transport, and the one that makes runtime swapping
/// practical:
/// </para>
/// <code>
/// using (TransportScope.Use("restsharp"))
///     await client.GetAsync(owner, name);
/// </code>
/// <para>
/// The alternative would be a transport parameter on every client method, which would put a
/// transport concern into the API contract of every interface — the thing those interfaces exist
/// to be free of.
/// </para>
/// <para>
/// The value flows with the async context, so it applies across awaits and into tasks started
/// inside the scope. It does not flow back out: a nested scope restores the outer one on dispose.
/// </para>
/// </remarks>
public static class TransportScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>
    /// The transport name selected by the innermost enclosing scope, or <see langword="null"/>
    /// when no scope is active.
    /// </summary>
    public static string? Current => Ambient.Value;

    /// <summary>Selects a transport until the returned value is disposed.</summary>
    /// <param name="name">The transport name, matching <see cref="IHttpTransport.Name"/>.</param>
    /// <returns>A value that restores the previous selection when disposed.</returns>
    public static IDisposable Use(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var previous = Ambient.Value;
        Ambient.Value = name;

        return new Scope(previous);
    }

    /// <summary>
    /// Clears any ambient selection until the returned value is disposed, so the client falls back
    /// to its configured transport.
    /// </summary>
    /// <returns>A value that restores the previous selection when disposed.</returns>
    public static IDisposable UseDefault()
    {
        var previous = Ambient.Value;
        Ambient.Value = null;

        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        internal Scope(string? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
