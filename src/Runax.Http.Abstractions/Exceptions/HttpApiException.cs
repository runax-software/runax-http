namespace Runax.Http;

/// <summary>
/// The server answered, and the answer was an error status code.
/// </summary>
/// <remarks>
/// <para>
/// Thrown when a client method that declares a plain return type gets a non-success status.
/// A method that declares <see cref="ApiResponse{T}"/> instead gets this on
/// <see cref="ApiResponse.Error"/> and nothing is thrown.
/// </para>
/// <para>
/// This type is the same whichever transport ran the request, which is the point: catching it
/// does not mean catching <c>RestSharp</c>'s exception in one place and
/// <see cref="System.Net.Http.HttpRequestException"/> in another, and swapping a transport does
/// not change what callers have to catch.
/// </para>
/// </remarks>
public class HttpApiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="HttpApiException"/> class.</summary>
    public HttpApiException() : this("The HTTP request returned an error status code.") { }

    /// <summary>Initializes a new instance of the <see cref="HttpApiException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public HttpApiException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="HttpApiException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public HttpApiException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>The status code that came back.</summary>
    public int StatusCode { get; init; }

    /// <summary>The reason phrase, where the transport exposed one.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>
    /// The response body as text, where it was read. Error bodies are usually small and almost
    /// always the thing you need in order to understand the failure, so they are read eagerly
    /// and kept here rather than left on a stream that has since been disposed.
    /// </summary>
    public string? ResponseContent { get; init; }

    /// <summary>The response headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = [];

    /// <summary>The method that was issued.</summary>
    public RequestMethod Method { get; init; }

    /// <summary>The address that was requested.</summary>
    public Uri? RequestUri { get; init; }

    /// <summary>The name of the transport that ran the request.</summary>
    public string? TransportName { get; init; }
}
