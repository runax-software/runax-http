using System.Net;
using RestSharp;
using RestContentType = RestSharp.ContentType;

namespace Runax.Http.Transports;

/// <summary>
/// Sends requests with RestSharp.
/// </summary>
/// <remarks>
/// <para>
/// The second transport, and the one that proves the abstraction holds: the same client interface
/// runs over this or over <c>HttpClient</c>, and nothing above
/// <see cref="IHttpTransport"/> changes.
/// </para>
/// <para>
/// It is also a working example of transports genuinely differing. RestSharp's
/// <c>ExecuteAsync</c> reads the whole response into <c>RawBytes</c> before returning, and its body
/// parameters take a value rather than a stream — so this transport declares neither
/// <see cref="TransportCapabilities.ResponseStreaming"/> nor
/// <see cref="TransportCapabilities.RequestStreaming"/>, where the <c>HttpClient</c> transport
/// declares both. A client that streams a large upload will be told so instead of quietly having
/// it buffered.
/// </para>
/// </remarks>
public sealed class RestSharpTransport : IHttpTransport, IDisposable
{
    /// <summary>The name this transport is selected by.</summary>
    public const string TransportName = "restsharp";

    /// <summary>
    /// What RestSharp can do on a default client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three absences, each for a reason rather than out of caution:
    /// </para>
    /// <para>
    /// <see cref="TransportCapabilities.RequestStreaming"/> — a RestSharp body parameter holds a
    /// value, not a stream. A streamed body would have to be read into memory first, and buffering
    /// an upload of unknown size without saying so is the quiet failure the capability model
    /// exists to prevent.
    /// </para>
    /// <para>
    /// <see cref="TransportCapabilities.ResponseStreaming"/> — <c>ExecuteAsync</c> reads the whole
    /// response before it returns. RestSharp does offer <c>DownloadStreamAsync</c>, but that gives
    /// back a bare stream with no status code or headers, which is not enough to build a
    /// <see cref="TransportResponse"/> from.
    /// </para>
    /// <para>
    /// <see cref="TransportCapabilities.PerRequestProxy"/> — a proxy belongs to
    /// <c>RestClientOptions</c>, so it is per client, exactly as with <c>HttpClient</c>. Rotating
    /// proxies means a transport instance per proxy, registered under different names.
    /// </para>
    /// <para>
    /// <see cref="TransportCapabilities.AutomaticDecompression"/> and
    /// <see cref="TransportCapabilities.Cookies"/> are added when
    /// <see cref="RestSharpTransportOptions"/> enables them, since both depend on configuration.
    /// </para>
    /// </remarks>
    public static readonly TransportCapabilities DefaultCapabilities =
        TransportCapabilities.PerRequestTimeout
        | TransportCapabilities.Multipart
        | TransportCapabilities.AutomaticRedirect
        | TransportCapabilities.Http2
        | TransportCapabilities.CancellationMidFlight;

    private readonly RestClient _client;
    private readonly bool _ownsClient;

    /// <summary>Initializes a new instance of the <see cref="RestSharpTransport"/> class.</summary>
    /// <param name="options">How to build the underlying client.</param>
    public RestSharpTransport(RestSharpTransportOptions? options = null)
    {
        var settings = options ?? new RestSharpTransportOptions();

        var clientOptions = new RestClientOptions
        {
            Proxy = settings.Proxy,
            FollowRedirects = settings.FollowRedirects,
            AutomaticDecompression = settings.AutomaticDecompression,
            CookieContainer = settings.CookieContainer,
            Timeout = settings.Timeout,

            // Errors have to arrive as responses, not exceptions: deciding what a status code means
            // belongs to the caller, which can see the client interface's declared return type.
            //
            // SetErrorExceptionOnUnsuccessfulStatusCode is RestSharp's default and has to be turned
            // off. With it on, a 404 arrives as ResponseStatus.Error with an ErrorException, which
            // is indistinguishable from "the host was never reached" — so a perfectly ordinary
            // error status would surface as a TransportException. The conformance suite caught this.
            ThrowOnAnyError = false,
            ThrowOnDeserializationError = false,
            SetErrorExceptionOnUnsuccessfulStatusCode = false
        };

        settings.ConfigureClient?.Invoke(clientOptions);

        Name = settings.Name;
        Capabilities = Describe(clientOptions, settings);

        _client = new RestClient(clientOptions);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RestSharpTransport"/> class over a client you
    /// built yourself.
    /// </summary>
    /// <param name="client">The client to send through. Not disposed by this transport.</param>
    /// <param name="name">The name this transport is selected by.</param>
    /// <param name="capabilities">
    /// What this client can actually do, given how it was configured. Defaults to
    /// <see cref="DefaultCapabilities"/>.
    /// </param>
    public RestSharpTransport(
        RestClient client,
        string name = TransportName,
        TransportCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(name);

        _client = client;
        _ownsClient = false;
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

        var restRequest = BuildRequest(request);
        RestResponse response;

        try
        {
            response = await _client.ExecuteAsync(restRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw Failed(request, exception.Message, exception);
        }

        // RestSharp reports transport-level trouble on the response rather than by throwing, so
        // the distinction between "no status code was received" and "the server said 503" has to be
        // read off the response rather than inferred.
        //
        // The StatusCode check is the load-bearing half: ResponseStatus.Error alone is not enough,
        // because a caller who re-enables SetErrorExceptionOnUnsuccessfulStatusCode through
        // ConfigureClient would otherwise turn every error status into a transport failure.
        if (response.ResponseStatus is ResponseStatus.Error or ResponseStatus.Aborted
            && response.StatusCode == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw Failed(
                request,
                response.ErrorMessage ?? response.ResponseStatus.ToString(),
                response.ErrorException);
        }

        if (response.ResponseStatus == ResponseStatus.TimedOut)
        {
            // Surfaced as a timeout rather than a cancellation: "the deadline passed" and "the
            // caller changed their mind" send retry logic down different paths.
            throw new TransportException(
                $"The request to {request.RequestUri} timed out"
                + (request.Timeout is { } limit ? $" after {limit}." : "."),
                response.ErrorException)
            {
                TransportName = Name,
                RequestUri = request.RequestUri
            };
        }

        return ToTransportResponse(response, request);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }

    private RestRequest BuildRequest(TransportRequest request)
    {
        // The URI arrives absolute and final, so it goes in as the resource with no base address on
        // the client. RestSharp would otherwise apply its own joining rules, and those differ from
        // HttpClient's — which is the whole reason URL composition happens above this boundary.
        var restRequest = new RestRequest(request.RequestUri, ToMethod(request.Method));

        if (request.Timeout is { } timeout)
            restRequest.Timeout = timeout;

        if (request.Version is not null)
            restRequest.Version = request.Version;

        foreach (var (name, value) in request.Headers)
        {
            // Content-Type is carried by the body parameter rather than as a header; adding it here
            // as well produces a duplicate that some servers reject.
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;

            restRequest.AddParameter(new HeaderParameter(name, value));
        }

        AddBody(restRequest, request);

        return restRequest;
    }

    private void AddBody(RestRequest restRequest, TransportRequest request)
    {
        if (request.Content is null)
            return;

        var contentType = ResolveContentType(request);

        switch (request.Content)
        {
            case BinaryContent binary:
                restRequest.AddParameter(new BodyParameter(
                    binary.Bytes.ToArray(),
                    contentType ?? RestContentType.Binary,
                    DataFormat.Binary));

                break;

            case FormContent form:
                {
                    foreach (var (key, value) in form.Fields)
                        restRequest.AddParameter(new GetOrPostParameter(key, value ?? string.Empty));

                    break;
                }

            case MultipartFormContent multipart:
                {
                    restRequest.AlwaysMultipartFormData = true;

                    foreach (var part in multipart.Parts)
                        AddMultipartPart(restRequest, part);

                    break;
                }

            case StreamedContent:
                // Declared as unsupported, so the executor rejects it before we are called. Here so
                // that enabling the capability without implementing it fails loudly.
                throw new TransportCapabilityException(
                    $"Transport '{Name}' cannot stream a request body.")
                {
                    Missing = TransportCapabilities.RequestStreaming,
                    TransportName = Name,
                    RequestUri = request.RequestUri
                };

            default:
                throw new TransportCapabilityException(
                    $"Transport '{Name}' does not know how to send {request.Content.GetType().Name}.")
                {
                    TransportName = Name,
                    RequestUri = request.RequestUri
                };
        }
    }

    private static void AddMultipartPart(RestRequest restRequest, MultipartPart part)
    {
        switch (part.Content)
        {
            case BinaryContent binary when part.FileName is not null:
                restRequest.AddFile(
                    part.Name,
                    binary.Bytes.ToArray(),
                    part.FileName,
                    part.Content.ContentType is { } type ? (RestContentType)type : null);

                break;

            case BinaryContent binary:
                restRequest.AddParameter(new GetOrPostParameter(
                    part.Name,
                    System.Text.Encoding.UTF8.GetString(binary.Bytes.Span)));

                break;

            default:
                throw new NotSupportedException(
                    $"A multipart part of type {part.Content.GetType().Name} is not supported.");
        }
    }

    private static RestContentType? ResolveContentType(TransportRequest request)
    {
        // An explicit Content-Type header wins over the content's own, since a method that sets one
        // did so deliberately.
        foreach (var (name, value) in request.Headers)
        {
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                return (RestContentType)value;
        }

        return request.Content?.ContentType is { } contentType ? (RestContentType)contentType : null;
    }

    private static TransportResponse ToTransportResponse(RestResponse response, TransportRequest request)
    {
        var headers = new List<KeyValuePair<string, string>>();

        Collect(response.Headers);
        Collect(response.ContentHeaders);

        // RawBytes rather than Content, so a non-UTF8 or binary body survives: decoding to a string
        // and re-encoding would corrupt it.
        var body = response.RawBytes is { Length: > 0 } bytes
            ? new MemoryStream(bytes, writable: false)
            : null;

        return new TransportResponse((int)response.StatusCode)
        {
            ReasonPhrase = response.StatusDescription,
            Headers = headers,
            Content = body,
            FinalUri = response.ResponseUri ?? request.RequestUri,
            Version = response.Version
        };

        void Collect(IReadOnlyCollection<HeaderParameter>? parameters)
        {
            if (parameters is null)
                return;

            foreach (var parameter in parameters)
            {
                if (parameter.Name is { } name && parameter.Value is { } value)
                    headers.Add(new KeyValuePair<string, string>(name, value.ToString() ?? string.Empty));
            }
        }
    }

    private TransportException Failed(TransportRequest request, string reason, Exception? inner)
        => new($"The request to {request.RequestUri} did not complete: {reason}", inner)
        {
            TransportName = Name,
            RequestUri = request.RequestUri
        };

    private static TransportCapabilities Describe(
        RestClientOptions options,
        RestSharpTransportOptions settings)
    {
        var capabilities = TransportCapabilities.PerRequestTimeout
                           | TransportCapabilities.Multipart
                           | TransportCapabilities.Http2
                           | TransportCapabilities.CancellationMidFlight;

        // Derived from the options actually in force rather than assumed, because both of these are
        // off unless asked for and claiming them regardless would be a lie about this instance.
        if (options.FollowRedirects)
            capabilities |= TransportCapabilities.AutomaticRedirect;

        if (options.AutomaticDecompression != DecompressionMethods.None)
            capabilities |= TransportCapabilities.AutomaticDecompression;

        if (settings.CookieContainer is not null)
            capabilities |= TransportCapabilities.Cookies;

        return capabilities;
    }

    internal static Method ToMethod(RequestMethod method) => method switch
    {
        RequestMethod.Get => Method.Get,
        RequestMethod.Post => Method.Post,
        RequestMethod.Put => Method.Put,
        RequestMethod.Patch => Method.Patch,
        RequestMethod.Delete => Method.Delete,
        RequestMethod.Head => Method.Head,
        RequestMethod.Options => Method.Options,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported HTTP method.")
    };
}
