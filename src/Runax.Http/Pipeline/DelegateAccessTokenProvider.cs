namespace Runax.Http.Pipeline;

/// <summary>An <see cref="IAccessTokenProvider"/> over a lambda.</summary>
/// <remarks>
/// For the common cases that do not deserve a class: a token read from configuration, or one
/// fetched by a method you already have.
/// </remarks>
public sealed class DelegateAccessTokenProvider : IAccessTokenProvider
{
    private readonly Func<RequestContext, CancellationToken, ValueTask<string?>> _provide;

    /// <summary>Initializes a new instance from an asynchronous lookup.</summary>
    /// <param name="provide">Produces the token.</param>
    public DelegateAccessTokenProvider(Func<RequestContext, CancellationToken, ValueTask<string?>> provide)
    {
        ArgumentNullException.ThrowIfNull(provide);

        _provide = provide;
    }

    /// <summary>Initializes a new instance from an asynchronous lookup that ignores the request.</summary>
    /// <param name="provide">Produces the token.</param>
    public DelegateAccessTokenProvider(Func<CancellationToken, ValueTask<string?>> provide)
    {
        ArgumentNullException.ThrowIfNull(provide);

        _provide = (_, cancellationToken) => provide(cancellationToken);
    }

    /// <summary>Initializes a new instance from a synchronous lookup.</summary>
    /// <param name="provide">
    /// Produces the token. Called on every attempt, so it should be a cache read rather than
    /// anything that blocks.
    /// </param>
    public DelegateAccessTokenProvider(Func<string?> provide)
    {
        ArgumentNullException.ThrowIfNull(provide);

        _provide = (_, _) => new ValueTask<string?>(provide());
    }

    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _provide(context, cancellationToken);
    }
}
