namespace Runax.Http.Pipeline;

/// <summary>Supplies the credential for an outgoing request.</summary>
/// <remarks>
/// <para>
/// Asked once per attempt, so a token that expired between the first try and a retry is refreshed
/// without the retry policy knowing anything about authentication.
/// </para>
/// <para>
/// <see cref="ValueTask{TResult}"/> rather than a pair of sync and async methods: a cached token
/// returns without allocating, and a fetch awaits, from the same signature. Implementations must be
/// thread-safe, and a provider that fetches should cache — this is on the path of every request.
/// </para>
/// </remarks>
public interface IAccessTokenProvider
{
    /// <summary>Gets the token to send.</summary>
    /// <param name="context">The request being sent, if the token depends on it.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The token, or <see langword="null"/> to send the request unauthenticated — which is how an
    /// anonymous call through an otherwise authenticated client is expressed.
    /// </returns>
    ValueTask<string?> GetTokenAsync(RequestContext context, CancellationToken cancellationToken);
}
