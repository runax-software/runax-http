namespace Runax.Http.Transports;

/// <summary>The default <see cref="IHttpTransportResolver"/>, over a fixed set of transports.</summary>
public sealed class HttpTransportResolver : IHttpTransportResolver
{
    private readonly Dictionary<string, IHttpTransport> _transports;
    private readonly IHttpTransport? _default;

    /// <summary>Initializes a new instance of the <see cref="HttpTransportResolver"/> class.</summary>
    /// <param name="transports">The registered transports.</param>
    /// <param name="defaultTransportName">
    /// The transport to use when a request names none. Optional when exactly one transport is
    /// registered — that one is the default — and required beyond that, because guessing which of
    /// several a caller meant is how a request quietly goes out over the wrong stack.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Two transports share a name, or <paramref name="defaultTransportName"/> names one that was
    /// not supplied.
    /// </exception>
    public HttpTransportResolver(IEnumerable<IHttpTransport> transports, string? defaultTransportName = null)
    {
        ArgumentNullException.ThrowIfNull(transports);

        _transports = new Dictionary<string, IHttpTransport>(StringComparer.OrdinalIgnoreCase);

        foreach (var transport in transports)
        {
            if (!_transports.TryAdd(transport.Name, transport))
                throw new ArgumentException(
                    $"More than one HTTP transport is registered under the name '{transport.Name}'. "
                    + "Transport names select which stack a request goes over, so they have to be unique.",
                    nameof(transports));
        }

        if (defaultTransportName is not null)
        {
            if (!_transports.TryGetValue(defaultTransportName, out _default))
                throw new ArgumentException(
                    $"'{defaultTransportName}' is configured as the default HTTP transport, but no transport "
                    + $"is registered under that name. Registered transports: "
                    + $"{(_transports.Count == 0 ? "none" : string.Join(", ", _transports.Keys))}.",
                    nameof(defaultTransportName));
        }
        else if (_transports.Count == 1)
        {
            // One transport and no explicit default is unambiguous, so it would be pedantic to
            // demand the name. Two or more without a default is genuinely ambiguous and fails
            // at Resolve time with a message that says so.
            foreach (var only in _transports.Values)
                _default = only;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Names => _transports.Keys;

    /// <inheritdoc />
    public IHttpTransport Resolve(string? name)
    {
        if (name is null)
            return _default ?? throw UnknownTransportException.ForMissingDefault(Names);

        return _transports.TryGetValue(name, out var transport)
            ? transport
            : throw UnknownTransportException.ForName(name, Names);
    }

    /// <inheritdoc />
    public bool TryResolve(string name, out IHttpTransport? transport)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _transports.TryGetValue(name, out transport);
    }
}
