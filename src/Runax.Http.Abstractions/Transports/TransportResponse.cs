namespace Runax.Http.Transports;

/// <summary>
/// What came back, described without reference to any HTTP library.
/// </summary>
/// <remarks>
/// A response owns transport resources — the underlying response object, a live body stream,
/// a pooled connection — so it is disposable and the caller must dispose it. Disposing it
/// disposes <see cref="Content"/>, which is why anything that needs the body past that point
/// has to read it first.
/// </remarks>
public sealed class TransportResponse : IAsyncDisposable, IDisposable
{
    private readonly IAsyncDisposable? _transportResource;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TransportResponse"/> class.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="transportResource">
    /// A transport-owned resource to dispose with this response — typically the HTTP library's
    /// own response object. Pass <see langword="null"/> when there is nothing to release.
    /// </param>
    public TransportResponse(int statusCode, IAsyncDisposable? transportResource = null)
    {
        StatusCode = statusCode;
        _transportResource = transportResource;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The reason phrase, where the transport exposes one.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>
    /// The response headers, including content headers. A list rather than a dictionary because
    /// headers such as <c>Set-Cookie</c> legitimately repeat.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>
    /// The response body, or <see langword="null"/> when there is none. Disposed with this
    /// response.
    /// </summary>
    /// <remarks>
    /// Whether this is a live network stream or a buffer already in memory depends on
    /// <see cref="TransportCapabilities.ResponseStreaming"/>. Read it as a stream either way and
    /// the client works on both.
    /// </remarks>
    public Stream? Content { get; init; }

    /// <summary>
    /// The address that actually answered, which differs from the request address when
    /// redirects were followed. <see langword="null"/> if the transport doesn't report it.
    /// </summary>
    public Uri? FinalUri { get; init; }

    /// <summary>The negotiated HTTP version, where the transport exposes it.</summary>
    public Version? Version { get; init; }

    /// <summary>Whether the status code is in the 2xx range.</summary>
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Content?.Dispose();

        // A transport resource that is only IAsyncDisposable has to be drained synchronously
        // here. Transports whose resource is also IDisposable should pass one that implements
        // both, so this path is not taken.
        switch (_transportResource)
        {
            case IDisposable disposable:
                disposable.Dispose();
                break;
            case not null:
                _transportResource.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (Content is not null)
            await Content.DisposeAsync().ConfigureAwait(false);

        if (_transportResource is not null)
            await _transportResource.DisposeAsync().ConfigureAwait(false);
    }
}
