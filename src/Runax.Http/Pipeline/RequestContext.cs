using Runax.Http.Requests;
using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>
/// One logical call as it passes through the pipeline: created once, and reused for every attempt.
/// </summary>
/// <remarks>
/// <para>
/// The lifetime is the point. A stage runs per attempt, so a token is re-read on a retry — but the
/// context is created per <em>call</em>, so anything that must stay the same across attempts lives
/// here instead of in the stage. <see cref="CorrelationId"/> is the worked example: assigned on the
/// first attempt and read back on the rest, so a retried request keeps one id while
/// <see cref="Attempt"/> distinguishes the tries.
/// </para>
/// <para>
/// That split is why the pipeline sits inside the retry loop rather than around it. Outside, a
/// refreshed token would never reach a retry; inside with nowhere to keep per-call state, a
/// correlation id would change on every try. Both positions are wrong on their own.
/// </para>
/// </remarks>
public sealed class RequestContext
{
    private readonly TransportRequest _request;
    private readonly List<KeyValuePair<string, string>> _headers;

    private Dictionary<string, object?>? _items;

    internal RequestContext(
        ClientRequest clientRequest,
        TransportRequest request,
        IHttpTransport transport)
    {
        ClientRequest = clientRequest;
        Transport = transport;
        _request = request;

        // Copied so a stage can add to it without mutating the request the retry loop holds.
        _headers = [.. request.Headers];
    }

    /// <summary>What the client method asked for, before any of it was resolved.</summary>
    public ClientRequest ClientRequest { get; }

    /// <summary>The transport that will send this request.</summary>
    public IHttpTransport Transport { get; }

    /// <summary>The HTTP method.</summary>
    public RequestMethod Method => _request.Method;

    /// <summary>The absolute address, with the path substituted and the query composed.</summary>
    public Uri RequestUri => _request.RequestUri;

    /// <summary>The body, already encoded, or <see langword="null"/> for a request without one.</summary>
    public RequestContent? Content => _request.Content;

    /// <summary>The per-request timeout, if one applies.</summary>
    public TimeSpan? Timeout => _request.Timeout;

    /// <summary>
    /// The headers to send. The one part of the request a stage may change — see
    /// <see cref="IHttpMiddleware.InvokeAsync"/> for why the rest is read-only.
    /// </summary>
    public IList<KeyValuePair<string, string>> Headers => _headers;

    /// <summary>
    /// Which attempt this is, counting from one. Above one means the retry policy is re-sending.
    /// </summary>
    /// <remarks>
    /// Set by the retry loop before each pass, so a stage can tell a first try from a retry — which
    /// is what lets a log line say which attempt it is describing.
    /// </remarks>
    public int Attempt { get; internal set; } = 1;

    /// <summary>
    /// The id identifying this call, stable across every attempt.
    /// </summary>
    /// <remarks>
    /// Assigned by the correlation stage on the first attempt and left alone afterwards, so the
    /// retries of one logical call share an id. <see langword="null"/> when correlation is off.
    /// </remarks>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Anything one stage needs to hand to another, or to a later attempt of the same call.
    /// </summary>
    /// <remarks>
    /// Created lazily, because most calls never touch it and a dictionary per request would be an
    /// allocation that bought nothing.
    /// </remarks>
    public IDictionary<string, object?> Items =>
        _items ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Reads a header's first value, or <see langword="null"/> if it is not set.</summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public string? GetHeader(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        foreach (var (key, value) in Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>Sets a header, replacing any values already there.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// Replaces rather than appends, which is what a stage re-running on a retry needs: appending
    /// would leave the previous attempt's token on the request alongside the new one.
    /// </remarks>
    public void SetHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        RemoveHeader(name);
        Headers.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>Adds a header without removing values already under that name.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value.</param>
    /// <remarks>For the headers that legitimately repeat. Prefer <see cref="SetHeader"/> otherwise.</remarks>
    public void AddHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        Headers.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>Removes every value under a header name.</summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <returns><see langword="true"/> if anything was removed.</returns>
    public bool RemoveHeader(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var removed = false;

        for (var i = Headers.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(Headers[i].Key, name, StringComparison.OrdinalIgnoreCase))
                continue;

            Headers.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    /// <summary>
    /// The request to hand the transport, with whatever the pipeline did to the headers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single place a <see cref="TransportRequest"/> is rebuilt from a context. If a property is
    /// ever added to <see cref="TransportRequest"/> it has to be copied here too, or the pipeline
    /// would quietly drop it — which is what <c>RequestContextTests.The_rebuilt_request_carries_every_property</c>
    /// is there to catch.
    /// </para>
    /// <para>
    /// The headers are snapshotted rather than handed over live. A <see cref="TransportRequest"/> is
    /// documented as immutable and transports are entitled to hold on to one — and on a retry the
    /// next attempt's <see cref="SetHeader"/> would otherwise rewrite the headers of a request that
    /// has already been sent.
    /// </para>
    /// </remarks>
    internal TransportRequest ToTransportRequest() => new()
    {
        Method = _request.Method,
        RequestUri = _request.RequestUri,
        Headers = [.. _headers],
        Content = _request.Content,
        Timeout = _request.Timeout,
        Proxy = _request.Proxy,
        Version = _request.Version
    };
}
