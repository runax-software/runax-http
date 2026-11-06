using System.Net.Http.Headers;
using Runax.Http.Requests;

namespace Runax.Http.Transports;

/// <summary>
/// The default transport, over <see cref="HttpClient"/>. Ships in <c>Runax.Http</c> rather than a
/// transport package of its own, because it needs nothing but the BCL and so costs a consumer no
/// extra dependency.
/// </summary>
public sealed class HttpClientTransport : IHttpTransport
{
    /// <summary>The name this transport is selected by.</summary>
    public const string TransportName = "httpclient";

    /// <summary>
    /// What a <see cref="HttpClient"/> on a stock handler can do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two notable absences, both honest rather than conservative:
    /// </para>
    /// <para>
    /// <see cref="TransportCapabilities.PerRequestProxy"/> — a proxy belongs to the handler, and
    /// the handler is shared by every request through this client. Claiming it and then ignoring
    /// <see cref="TransportRequest.Proxy"/> would send a request direct that the caller asked to
    /// route, which is exactly the quiet failure the capability model exists to prevent. Rotating
    /// proxies with this transport means a client per proxy; a transport whose library takes a
    /// proxy per request can declare it.
    /// </para>
    /// <para>
    /// <see cref="TransportCapabilities.AutomaticDecompression"/> — off by default on
    /// <see cref="SocketsHttpHandler"/>. If you enable it, say so with the
    /// <see cref="HttpClientTransport(HttpClient, string, TransportCapabilities?)"/> overload
    /// rather than assuming.
    /// </para>
    /// </remarks>
    public static readonly TransportCapabilities DefaultCapabilities =
        TransportCapabilities.RequestStreaming
        | TransportCapabilities.ResponseStreaming
        | TransportCapabilities.PerRequestTimeout
        | TransportCapabilities.Multipart
        | TransportCapabilities.AutomaticRedirect
        | TransportCapabilities.Cookies
        | TransportCapabilities.Http2
        | TransportCapabilities.CancellationMidFlight;

    private readonly HttpClient _client;

    /// <summary>Initializes a new instance of the <see cref="HttpClientTransport"/> class.</summary>
    /// <param name="client">
    /// The client to send through. Its <see cref="HttpClient.BaseAddress"/> is not used — the
    /// request URI arrives absolute — and its <see cref="HttpClient.Timeout"/> applies only to
    /// requests that do not carry one of their own.
    /// </param>
    /// <param name="name">The name this transport is selected by.</param>
    /// <param name="capabilities">
    /// What this client can actually do, when its handler has been configured away from the
    /// defaults. Defaults to <see cref="DefaultCapabilities"/>.
    /// </param>
    public HttpClientTransport(
        HttpClient client,
        string name = TransportName,
        TransportCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _client = client;
        Name = name;
        Capabilities = capabilities ?? DefaultCapabilities;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TransportCapabilities Capabilities { get; }

    /// <inheritdoc />
    public async Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = BuildMessage(request);

        // A per-request timeout has to be a linked token: HttpClient.Timeout is per-client, so
        // setting it here would race every other request going through the same instance.
        using var timeout = request.Timeout is { } limit
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        timeout?.CancelAfter(request.Timeout!.Value);

        HttpResponseMessage response;

        try
        {
            // ResponseHeadersRead, so a caller that wants to stream a large body can, instead of
            // the whole thing being buffered before the first byte is visible.
            response = await _client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout?.Token ?? cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && timeout?.IsCancellationRequested == true)
        {
            // The caller's token did not fire, so this was our own per-request deadline. Surfacing
            // it as a cancellation would read as "the caller changed their mind", which sends
            // retry logic down the wrong path.
            throw new TransportException(
                $"The request to {request.RequestUri} timed out after {request.Timeout}.")
            {
                TransportName = Name,
                RequestUri = request.RequestUri
            };
        }
        catch (HttpRequestException exception)
        {
            throw new TransportException(
                $"The request to {request.RequestUri} did not complete: {exception.Message}",
                exception)
            {
                TransportName = Name,
                RequestUri = request.RequestUri
            };
        }

        return await ToTransportResponseAsync(response).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildMessage(TransportRequest request)
    {
        var message = new HttpRequestMessage(ToHttpMethod(request.Method), request.RequestUri);

        if (request.Version is not null)
            message.Version = request.Version;

        if (request.Content is not null)
            message.Content = ToHttpContent(request.Content);

        foreach (var (name, value) in request.Headers)
        {
            // Content headers (Content-Type, Content-Length, ...) are rejected by the request
            // header collection, so each header goes wherever HttpClient will accept it. TryAdd
            // rather than Add, because Add throws on a header it considers malformed and the
            // server is a better judge of that than our allow-list.
            if (message.Headers.TryAddWithoutValidation(name, value))
                continue;

            message.Content?.Headers.TryAddWithoutValidation(name, value);
        }

        return message;
    }

    private static HttpContent ToHttpContent(RequestContent content)
    {
        HttpContent http = content switch
        {
            BinaryContent binary => new ReadOnlyMemoryContent(binary.Bytes),
            StreamedContent streamed => new StreamContent(streamed.Stream),
            FormContent form => BuildForm(form),
            MultipartFormContent multipart => BuildMultipart(multipart),

            // Unreachable while the hierarchy stays closed to this assembly; here so that adding a
            // kind without teaching the transports about it fails loudly instead of silently
            // sending no body at all.
            _ => throw new TransportCapabilityException(
                $"The {nameof(HttpClientTransport)} does not know how to send {content.GetType().Name}.")
        };

        if (content.ContentType is { } mediaType && http.Headers.ContentType is null)
            http.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);

        return http;
    }

    private static FormUrlEncodedContent BuildForm(FormContent form)
    {
        var fields = new List<KeyValuePair<string, string>>(form.Fields.Count);

        foreach (var (key, value) in form.Fields)
            fields.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));

        return new FormUrlEncodedContent(fields);
    }

    private static MultipartFormDataContent BuildMultipart(MultipartFormContent multipart)
    {
        var content = new MultipartFormDataContent();

        foreach (var part in multipart.Parts)
        {
            var partContent = ToHttpContent(part.Content);

            if (part.FileName is null)
                content.Add(partContent, part.Name);
            else
                content.Add(partContent, part.Name, part.FileName);
        }

        return content;
    }

    private static async Task<TransportResponse> ToTransportResponseAsync(HttpResponseMessage response)
    {
        var headers = new List<KeyValuePair<string, string>>();

        foreach (var (name, values) in response.Headers)
            foreach (var value in values)
                headers.Add(new KeyValuePair<string, string>(name, value));

        foreach (var (name, values) in response.Content.Headers)
            foreach (var value in values)
                headers.Add(new KeyValuePair<string, string>(name, value));

        var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        // The HttpResponseMessage is handed over as the response's transport resource, so the
        // body stream stays usable until the caller disposes the TransportResponse. Disposing it
        // here would close the stream we just handed out.
        return new TransportResponse((int)response.StatusCode, new ResponseHandle(response))
        {
            ReasonPhrase = response.ReasonPhrase,
            Headers = headers,
            Content = body,
            FinalUri = response.RequestMessage?.RequestUri,
            Version = response.Version
        };
    }

    internal static HttpMethod ToHttpMethod(RequestMethod method) => method switch
    {
        RequestMethod.Get => HttpMethod.Get,
        RequestMethod.Post => HttpMethod.Post,
        RequestMethod.Put => HttpMethod.Put,
        RequestMethod.Patch => HttpMethod.Patch,
        RequestMethod.Delete => HttpMethod.Delete,
        RequestMethod.Head => HttpMethod.Head,
        RequestMethod.Options => HttpMethod.Options,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported HTTP method.")
    };

    /// <summary>
    /// Adapts <see cref="HttpResponseMessage"/> to the disposal contract
    /// <see cref="TransportResponse"/> expects. Implements both forms so the synchronous path is
    /// a real dispose rather than an async one drained on the calling thread.
    /// </summary>
    private sealed class ResponseHandle : IAsyncDisposable, IDisposable
    {
        private readonly HttpResponseMessage _response;

        internal ResponseHandle(HttpResponseMessage response) => _response = response;

        public void Dispose() => _response.Dispose();

        public ValueTask DisposeAsync()
        {
            _response.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
