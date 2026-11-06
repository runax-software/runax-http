namespace Runax.Http;

/// <summary>
/// A response as a value rather than an exception. Declare it as a client method's return type
/// when a non-success status is an expected outcome instead of a fault.
/// </summary>
/// <remarks>
/// The usual case for this is a 404 that means "no such thing" rather than "something is
/// broken", and a 409 or 422 whose body carries the validation detail you actually want. Both
/// read badly as exceptions.
/// </remarks>
public class ApiResponse
{
    /// <summary>Initializes a new instance of the <see cref="ApiResponse"/> class.</summary>
    /// <param name="statusCode">The status code that came back.</param>
    public ApiResponse(int statusCode) => StatusCode = statusCode;

    /// <summary>The status code that came back.</summary>
    public int StatusCode { get; }

    /// <summary>The reason phrase, where the transport exposed one.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>The response headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Whether the status code is in the 2xx range.</summary>
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    /// <summary>
    /// The error, when the status code was not a success — the same exception that would have
    /// been thrown had the method declared a plain return type. <see langword="null"/> on
    /// success.
    /// </summary>
    public HttpApiException? Error { get; init; }

    /// <summary>The name of the transport that ran the request.</summary>
    public string? TransportName { get; init; }
}

/// <summary>A response as a value, carrying a deserialized body.</summary>
/// <typeparam name="T">The response body type.</typeparam>
public sealed class ApiResponse<T> : ApiResponse
{
    /// <summary>Initializes a new instance of the <see cref="ApiResponse{T}"/> class.</summary>
    /// <param name="statusCode">The status code that came back.</param>
    public ApiResponse(int statusCode) : base(statusCode) { }

    /// <summary>
    /// The deserialized body. <see langword="null"/> when the status was an error, when the
    /// body was empty, or when the body deserialized to null.
    /// </summary>
    public T? Content { get; init; }

    /// <summary>
    /// Returns the body, or throws the error that came with a non-success status.
    /// </summary>
    /// <returns>The deserialized body.</returns>
    /// <exception cref="HttpApiException">The status code was not a success.</exception>
    /// <exception cref="InvalidOperationException">
    /// The status was a success but carried no body.
    /// </exception>
    public T EnsureSuccess()
    {
        if (Error is not null)
            throw Error;

        if (Content is null)
            throw new InvalidOperationException(
                $"The response succeeded with status {StatusCode} but carried no body to return.");

        return Content;
    }
}
