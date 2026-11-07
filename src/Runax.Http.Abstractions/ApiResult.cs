namespace Runax.Http;

/// <summary>
/// A response whose type was decided at run time, by status code or by what the body turned out to
/// contain. The return type for a method carrying <see cref="ResponseAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is typed as <see cref="object"/> because the whole point is that the shape
/// is not known until the response arrives. C# has no union type, so a pattern match is how you
/// get the static typing back — and it reads well:
/// </para>
/// <code>
/// var result = await client.GetAsync(id);
///
/// return result.Value switch
/// {
///     Thing thing          => Ok(thing),
///     ValidationProblem p  => BadRequest(p),
///     NotFound             => NotFound(),
///     _                    => Problem($"Unrecognized response: {result.UnmatchedContent}")
/// };
/// </code>
/// <para>
/// Nothing here throws. A method declaring <see cref="ApiResult"/> has said that an error status
/// is one of the expected answers, so it is reported rather than raised — use
/// <see cref="Require{T}"/> at the point where you do want the failure to be loud.
/// </para>
/// </remarks>
public class ApiResult
{
    /// <summary>Initializes a new instance of the <see cref="ApiResult"/> class.</summary>
    /// <param name="statusCode">The status code that came back.</param>
    public ApiResult(int statusCode) => StatusCode = statusCode;

    /// <summary>The status code that came back.</summary>
    public int StatusCode { get; }

    /// <summary>The reason phrase, where the transport exposed one.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>The response headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>The name of the transport that ran the request.</summary>
    public string? TransportName { get; init; }

    /// <summary>
    /// The deserialized body, as whichever type matched. <see langword="null"/> when nothing
    /// matched, or when the matching response had no body.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// The type <see cref="Value"/> was deserialized as, set even when the body was empty and
    /// <see cref="Value"/> is therefore null. <see langword="null"/> only when nothing matched.
    /// </summary>
    public Type? ValueType { get; init; }

    /// <summary>
    /// The body as text, kept only when no declared response matched.
    /// </summary>
    /// <remarks>
    /// This is the one case where you need the raw body: the API answered with something the
    /// method does not describe, and the shape of that something is the only clue as to what to
    /// add. Retained for that case and dropped otherwise, rather than kept on every response.
    /// </remarks>
    public string? UnmatchedContent { get; init; }

    /// <summary>Whether the status code is in the 2xx range.</summary>
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    /// <summary>
    /// Whether a declared response matched. <see langword="false"/> means the API answered with
    /// something this method does not describe; see <see cref="UnmatchedContent"/>.
    /// </summary>
    public bool Matched => ValueType is not null;

    /// <summary>Whether the body was deserialized as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type to test for.</typeparam>
    /// <returns><see langword="true"/> if <see cref="Value"/> is a <typeparamref name="T"/>.</returns>
    public bool Is<T>() => Value is T;

    /// <summary>Gets the body if it was deserialized as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type to get.</typeparam>
    /// <param name="value">The body, when it is a <typeparamref name="T"/>.</param>
    /// <returns><see langword="true"/> if the body is a <typeparamref name="T"/>.</returns>
    public bool TryGet<T>(out T? value)
    {
        if (Value is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the body as <typeparamref name="T"/>, or throws if it is something else.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The body.</returns>
    /// <exception cref="HttpApiException">
    /// The response was a different declared type, or nothing declared matched it.
    /// </exception>
    /// <remarks>
    /// For the call site that has already handled the alternatives and wants the happy path
    /// without another null check. The exception carries the status and the body, because "it
    /// wasn't a Thing" is not on its own enough to work out what it was.
    /// </remarks>
    public T Require<T>()
    {
        if (Value is T typed)
            return typed;

        var describe = Matched
            ? $"the response was deserialized as {ValueType}"
            : "no declared response matched it";

        throw new HttpApiException(
            $"Expected a {typeof(T)} from the {StatusCode} response, but {describe}.")
        {
            StatusCode = StatusCode,
            ReasonPhrase = ReasonPhrase,
            ResponseContent = UnmatchedContent,
            ResponseHeaders = Headers,
            TransportName = TransportName
        };
    }
}
