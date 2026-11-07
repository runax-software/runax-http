using System.Text;
using Runax.Http.Transports;

namespace Runax.Http.Testing;

/// <summary>
/// A request the test transport was asked to send, in a shape convenient to assert on.
/// </summary>
/// <remarks>
/// Wraps <see cref="TransportRequest"/> rather than exposing it directly, because what a test wants
/// to say is "the Authorization header was this" and "the body was that JSON" — not navigate a
/// header list and decode a byte buffer first.
/// </remarks>
public sealed class RecordedRequest
{
    private readonly TransportRequest _request;

    internal RecordedRequest(TransportRequest request) => _request = request;

    /// <summary>The underlying request, for anything the helpers here do not cover.</summary>
    public TransportRequest Request => _request;

    /// <summary>The HTTP method.</summary>
    public RequestMethod Method => _request.Method;

    /// <summary>The absolute address, with the path substituted and the query composed.</summary>
    public Uri Uri => _request.RequestUri;

    /// <summary>The path, without the query string.</summary>
    public string Path => _request.RequestUri.AbsolutePath;

    /// <summary>The query string, including the leading <c>?</c>, or empty if there was none.</summary>
    public string QueryString => _request.RequestUri.Query;

    /// <summary>The request headers, in the order they were added.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers => _request.Headers;

    /// <summary>The per-request timeout, if one was set.</summary>
    public TimeSpan? Timeout => _request.Timeout;

    /// <summary>The body, or <see langword="null"/> for a request without one.</summary>
    public RequestContent? Content => _request.Content;

    /// <summary>
    /// The body as text, for the common case of asserting on serialized JSON.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when there was no body. A streamed body is read here, which consumes
    /// the stream — a test that asserts on it is not going on to send it.
    /// </remarks>
    public string? Body => _request.Content switch
    {
        BinaryContent binary => Encoding.UTF8.GetString(binary.Bytes.Span),
        StreamedContent streamed => Read(streamed.Stream),
        FormContent form => string.Join(
            "&",
            form.Fields.Select(entry => $"{entry.Key}={entry.Value}")),
        _ => null
    };

    /// <summary>A header's first value, or <see langword="null"/> if it was not sent.</summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public string? Header(string name)
    {
        foreach (var (key, value) in _request.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>Every value sent under a header name, for the ones that legitimately repeat.</summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <returns>The values, in order.</returns>
    public IReadOnlyList<string> HeaderValues(string name)
        => _request.Headers
            .Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value)
            .ToList();

    /// <summary>
    /// A query parameter's first value, decoded.
    /// </summary>
    /// <param name="name">The query key.</param>
    /// <returns>The value, or <see langword="null"/> if the key was not sent.</returns>
    public string? Query(string name)
    {
        var values = QueryValues(name);

        return values.Count > 0 ? values[0] : null;
    }

    /// <summary>
    /// Every value sent under a query key, decoded. A collection argument expands to repeated keys,
    /// so this is how to assert on one.
    /// </summary>
    /// <param name="name">The query key.</param>
    /// <returns>The values, in order.</returns>
    public IReadOnlyList<string> QueryValues(string name)
    {
        var values = new List<string>();
        var query = QueryString.TrimStart('?');

        if (query.Length == 0)
            return values;

        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];

            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
                continue;

            values.Add(separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]));
        }

        return values;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Method.ToString().ToUpperInvariant()} {Uri}";

    private static string Read(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        return reader.ReadToEnd();
    }
}
