namespace Runax.Http;

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
