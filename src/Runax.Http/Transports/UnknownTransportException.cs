namespace Runax.Http;

/// <summary>
/// A transport was asked for by a name nothing is registered under.
/// </summary>
/// <remarks>
/// The message lists what <em>is</em> registered, because this is almost always one of two
/// things: a typo, or a missing transport package. Both are answered by seeing the available
/// names — "restsharp is not registered; available: httpclient" tells you immediately whether
/// you misspelled it or forgot to call <c>AddRestSharpTransport()</c>.
/// </remarks>
public sealed class UnknownTransportException : TransportException
{
    /// <summary>Initializes a new instance of the <see cref="UnknownTransportException"/> class.</summary>
    public UnknownTransportException() : this("No HTTP transport is registered under that name.") { }

    /// <summary>Initializes a new instance of the <see cref="UnknownTransportException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public UnknownTransportException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="UnknownTransportException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public UnknownTransportException(string message, Exception? innerException)
        : base(message, innerException) { }

    /// <summary>The name that was asked for, when one was given.</summary>
    public string? RequestedName { get; init; }

    /// <summary>The names that are registered.</summary>
    public IReadOnlyCollection<string> AvailableNames { get; init; } = [];

    internal static UnknownTransportException ForName(string name, IReadOnlyCollection<string> available)
        => new($"No HTTP transport named '{name}' is registered. {Describe(available)}")
        {
            RequestedName = name,
            AvailableNames = available
        };

    internal static UnknownTransportException ForMissingDefault(IReadOnlyCollection<string> available)
        => new(
            available.Count > 1
                ? "No default HTTP transport is configured, and more than one is registered, so there is "
                  + $"nothing to fall back to. {Describe(available)} Choose one with "
                  + "UseTransport(...) at registration, [Transport] on the interface, or TransportScope.Use(...)."
                : $"No HTTP transport is registered. {Describe(available)} Call AddRunaxHttp() to register "
                  + "the built-in HttpClient transport, or add a transport package.")
        {
            AvailableNames = available
        };

    private static string Describe(IReadOnlyCollection<string> available)
        => available.Count == 0
            ? "No transports are registered."
            : $"Registered transports: {string.Join(", ", available)}.";
}
