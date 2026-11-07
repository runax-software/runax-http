using System.Text;
using Runax.Http.Transports;

namespace Runax.Http.Testing;

/// <summary>A response for the test transport to answer with.</summary>
public sealed class TestResponse
{
    private TestResponse(int statusCode) => StatusCode = statusCode;

    /// <summary>The status code to return.</summary>
    public int StatusCode { get; }

    /// <summary>The reason phrase to return.</summary>
    public string? ReasonPhrase { get; private init; }

    /// <summary>The headers to return.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; private init; } = [];

    /// <summary>The body to return.</summary>
    public ReadOnlyMemory<byte> Body { get; private init; }

    /// <summary>A JSON body with a 200, which is what most tests want.</summary>
    /// <param name="json">The body.</param>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The response.</returns>
    public static TestResponse Json(string json, int statusCode = 200)
    {
        ArgumentNullException.ThrowIfNull(json);

        return new TestResponse(statusCode)
        {
            Body = Encoding.UTF8.GetBytes(json),
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")]
        };
    }

    /// <summary>A text body.</summary>
    /// <param name="text">The body.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="contentType">The content type to report.</param>
    /// <returns>The response.</returns>
    public static TestResponse Text(string text, int statusCode = 200, string contentType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(text);

        return new TestResponse(statusCode)
        {
            Body = Encoding.UTF8.GetBytes(text),
            Headers = [new KeyValuePair<string, string>("Content-Type", contentType)]
        };
    }

    /// <summary>A binary body.</summary>
    /// <param name="bytes">The body.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="contentType">The content type to report.</param>
    /// <returns>The response.</returns>
    public static TestResponse Bytes(
        ReadOnlyMemory<byte> bytes,
        int statusCode = 200,
        string contentType = "application/octet-stream")
        => new(statusCode)
        {
            Body = bytes,
            Headers = [new KeyValuePair<string, string>("Content-Type", contentType)]
        };

    /// <summary>A status code with no body.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The response.</returns>
    public static TestResponse Status(int statusCode) => new(statusCode);

    /// <summary>A status code with a body, for testing error handling.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body.</param>
    /// <param name="contentType">The content type to report.</param>
    /// <returns>The response.</returns>
    public static TestResponse Error(int statusCode, string body = "", string contentType = "application/json")
        => new(statusCode)
        {
            Body = Encoding.UTF8.GetBytes(body),
            Headers = [new KeyValuePair<string, string>("Content-Type", contentType)]
        };

    /// <summary>Adds a header to this response.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>A new response with the header added.</returns>
    public TestResponse WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        return new TestResponse(StatusCode)
        {
            ReasonPhrase = ReasonPhrase,
            Body = Body,
            Headers = [.. Headers, new KeyValuePair<string, string>(name, value)]
        };
    }

    /// <summary>Sets the reason phrase.</summary>
    /// <param name="reasonPhrase">The reason phrase.</param>
    /// <returns>A new response with the reason phrase set.</returns>
    public TestResponse WithReason(string reasonPhrase) => new(StatusCode)
    {
        ReasonPhrase = reasonPhrase,
        Body = Body,
        Headers = Headers
    };

    internal TransportResponse ToTransportResponse() => new(StatusCode)
    {
        ReasonPhrase = ReasonPhrase,
        Headers = Headers,
        Content = Body.IsEmpty ? null : new MemoryStream(Body.ToArray(), writable: false)
    };
}
