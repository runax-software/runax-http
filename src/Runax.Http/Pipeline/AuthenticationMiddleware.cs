using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>
/// Attaches a credential to each attempt.
/// </summary>
/// <remarks>
/// <para>
/// Innermost by default, and asked for a token on <em>every</em> attempt rather than once per call.
/// That is the whole reason the pipeline runs inside the retry loop: a token that expired between
/// the first try and a retry is refreshed by the provider, and the retry policy needs to know
/// nothing about authentication to benefit.
/// </para>
/// <para>
/// The header is replaced rather than appended, so a second attempt does not arrive carrying both
/// the stale credential and the fresh one.
/// </para>
/// </remarks>
public sealed class AuthenticationMiddleware : IHttpMiddleware
{
    /// <summary>The header a credential is written to unless told otherwise.</summary>
    public const string DefaultHeaderName = "Authorization";

    /// <summary>The scheme prepended to the token unless told otherwise.</summary>
    public const string DefaultScheme = "Bearer";

    private readonly IAccessTokenProvider _tokens;
    private readonly string _headerName;
    private readonly string? _scheme;

    /// <summary>Initializes a new instance of the <see cref="AuthenticationMiddleware"/> class.</summary>
    /// <param name="tokens">Supplies the credential.</param>
    /// <param name="headerName">The header to write. Defaults to <c>Authorization</c>.</param>
    /// <param name="scheme">
    /// The scheme to prepend, as in <c>Bearer &lt;token&gt;</c>. Pass <see langword="null"/> to
    /// send the token on its own, which is what an <c>X-Api-Key</c>-style header wants.
    /// </param>
    public AuthenticationMiddleware(
        IAccessTokenProvider tokens,
        string headerName = DefaultHeaderName,
        string? scheme = DefaultScheme)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentException.ThrowIfNullOrEmpty(headerName);

        _tokens = tokens;
        _headerName = headerName;
        _scheme = scheme;
    }

    /// <inheritdoc />
    public int Order => HttpMiddlewareOrder.Authentication;

    /// <inheritdoc />
    public async Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var token = await _tokens.GetTokenAsync(context, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(token))
        {
            context.SetHeader(
                _headerName,
                string.IsNullOrEmpty(_scheme) ? token : $"{_scheme} {token}");
        }
        else
        {
            // A provider returning null means "send this one unauthenticated". Any header left over
            // from a previous attempt has to go, or the request would not be unauthenticated at all.
            context.RemoveHeader(_headerName);
        }

        return await continuation(context, cancellationToken).ConfigureAwait(false);
    }
}
