using System.Text;
using Runax.Http.Requests;
using Runax.Http.Retry;
using Runax.Http.Serialization;
using Runax.Http.Transports;

namespace Runax.Http;

/// <inheritdoc cref="IHttpContractExecutor" />
public sealed class HttpContractExecutor : IHttpContractExecutor
{
    private readonly RunaxClientOptions _options;
    private readonly IHttpTransportResolver _transports;
    private readonly IContentSerializer _serializer;

    /// <summary>Initializes a new instance of the <see cref="HttpContractExecutor"/> class.</summary>
    /// <param name="options">How the client this executor serves is configured.</param>
    /// <param name="transports">The registered transports.</param>
    /// <param name="serializer">
    /// The application-wide serializer, used unless the client configures its own.
    /// </param>
    public HttpContractExecutor(
        RunaxClientOptions options,
        IHttpTransportResolver transports,
        IContentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentNullException.ThrowIfNull(serializer);

        _options = options;
        _transports = transports;
        _serializer = options.Serializer ?? serializer;
    }

    /// <inheritdoc />
    public IContentSerializer Serializer => _serializer;

    /// <inheritdoc />
    public async Task SendAsync(ClientRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.Transport.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(request, response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T?> SendAsync<T>(ClientRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.Transport.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(request, response, cancellationToken).ConfigureAwait(false);

        return await DeserializeAsync<T>(response.Transport, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiResponse> SendForApiResponseAsync(
        ClientRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        return new ApiResponse(response.Transport.StatusCode)
        {
            ReasonPhrase = response.Transport.ReasonPhrase,
            Headers = response.Transport.Headers,
            TransportName = response.TransportName,
            Error = response.Transport.IsSuccessStatusCode
                ? null
                : await CreateApiExceptionAsync(request, response, cancellationToken).ConfigureAwait(false)
        };
    }

    /// <inheritdoc />
    public async Task<ApiResponse<T>> SendForApiResponseAsync<T>(
        ClientRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.Transport.IsSuccessStatusCode)
        {
            return new ApiResponse<T>(response.Transport.StatusCode)
            {
                ReasonPhrase = response.Transport.ReasonPhrase,
                Headers = response.Transport.Headers,
                TransportName = response.TransportName,
                Error = await CreateApiExceptionAsync(request, response, cancellationToken).ConfigureAwait(false)
            };
        }

        return new ApiResponse<T>(response.Transport.StatusCode)
        {
            ReasonPhrase = response.Transport.ReasonPhrase,
            Headers = response.Transport.Headers,
            TransportName = response.TransportName,
            Content = await DeserializeAsync<T>(response.Transport, cancellationToken).ConfigureAwait(false)
        };
    }

    /// <inheritdoc />
    public async Task<ResponseEnvelope> SendForEnvelopeAsync(
        ClientRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        var content = await ReadToEndAsync(response.Transport, cancellationToken).ConfigureAwait(false);

        return new ResponseEnvelope(response.Transport.StatusCode, content)
        {
            ReasonPhrase = response.Transport.ReasonPhrase,
            Headers = response.Transport.Headers,
            TransportName = response.TransportName,
            RequestUri = response.RequestUri,
            Method = request.Method
        };
    }

    /// <inheritdoc />
    public async ValueTask<ApiResult> ToResultAsync<T>(
        ResponseEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        T? value = default;

        if (!envelope.Content.IsEmpty)
        {
            using var stream = new MemoryStream(
                envelope.Content.ToArray(), writable: false);

            value = await _serializer
                .DeserializeAsync<T>(stream, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ApiResult(envelope.StatusCode)
        {
            ReasonPhrase = envelope.ReasonPhrase,
            Headers = envelope.Headers,
            TransportName = envelope.TransportName,
            Value = value,

            // Set even when the body was empty, so a 204 that matched a declared response is
            // distinguishable from a response nothing matched at all.
            ValueType = typeof(T)
        };
    }

    /// <inheritdoc />
    public ApiResult Unmatched(ResponseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new ApiResult(envelope.StatusCode)
        {
            ReasonPhrase = envelope.ReasonPhrase,
            Headers = envelope.Headers,
            TransportName = envelope.TransportName,

            // The body is the only clue as to what the API actually sent, and therefore what
            // [Response] is missing. Kept here and nowhere else.
            UnmatchedContent = envelope.ReadAsString()
        };
    }

    private static async Task<ReadOnlyMemory<byte>> ReadToEndAsync(
        TransportResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
            return ReadOnlyMemory<byte>.Empty;

        using var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private async Task<Exchange> SendCoreAsync(ClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transport = _transports.Resolve(SelectTransportName(request));

        var transportRequest = new TransportRequest
        {
            Method = request.Method,
            RequestUri = RequestUriFactory.Create(_options.BaseAddress, request),
            Headers = BuildHeaders(request),
            Content = request.Content,
            Timeout = request.Timeout ?? _options.Timeout,
            Proxy = request.Proxy
        };

        // Checked here rather than inside each transport, so every transport gets the same
        // behaviour for free and a new one cannot forget to do it.
        var gap = TransportCapabilityException.ForMissing(
            transport,
            transportRequest.RequiredCapabilities,
            transportRequest.RequestUri);

        if (gap is not null)
            throw gap;

        // Resolved here so the retry loop below needs no knowledge of where its settings came
        // from: a method's [Retry] refines the client's options, and Suppressed beats both.
        var retry = _options.Retry.Resolve(request.Retry);

        return await SendWithRetriesAsync(transport, transportRequest, retry, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the request, repeating it while the settings allow and the outcome warrants it.
    /// </summary>
    /// <remarks>
    /// Above the transport on purpose. Every transport gets the same retry behaviour, and none of
    /// them has to implement it — which also means retry semantics do not change when you swap one.
    /// </remarks>
    private static async Task<Exchange> SendWithRetriesAsync(
        IHttpTransport transport,
        TransportRequest request,
        RetryOptions retry,
        CancellationToken cancellationToken)
    {
        var retryable = RetryPlanner.IsRetryable(retry, request);
        var attempt = 0;

        while (true)
        {
            attempt++;

            TransportResponse response;

            try
            {
                response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TransportException) when (retryable
                                             && retry.RetryTransportFailures
                                             && attempt < retry.MaxAttempts
                                             && !cancellationToken.IsCancellationRequested)
            {
                // The request never reached a status code, which is the case retrying is actually
                // for. No Retry-After to consult, so the backoff curve decides.
                var failureDelay = RetryPlanner.ComputeDelay(retry, attempt, retryAfter: null);

                if (failureDelay is null)
                    throw;

                await DelayAsync(retry, failureDelay.Value, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!retryable
                || attempt >= retry.MaxAttempts
                || !RetryPlanner.ShouldRetryStatus(retry, response.StatusCode))
            {
                return new Exchange(response, transport.Name, request.RequestUri);
            }

            var retryAfter = RetryPlanner.ReadRetryAfter(
                response.Headers, retry.TimeProvider.GetUtcNow());

            var delay = RetryPlanner.ComputeDelay(retry, attempt, retryAfter);

            if (delay is null)
            {
                // The server asked for longer than we are willing to wait. That is a refusal to
                // retry, not an instruction to block, so the response is returned as the answer.
                return new Exchange(response, transport.Name, request.RequestUri);
            }

            // Disposed before waiting, so a pooled connection is not held for the whole backoff.
            await response.DisposeAsync().ConfigureAwait(false);

            await DelayAsync(retry, delay.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task DelayAsync(RetryOptions retry, TimeSpan delay, CancellationToken cancellationToken)
        => delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, retry.TimeProvider, cancellationToken);

    /// <summary>
    /// Applies the transport precedence: ambient scope, then registration, then what the source
    /// declared, then the resolver's default.
    /// </summary>
    private string? SelectTransportName(ClientRequest request)
        => TransportScope.Current ?? _options.TransportName ?? request.TransportName;

    private List<KeyValuePair<string, string>> BuildHeaders(ClientRequest request)
    {
        var headers = new List<KeyValuePair<string, string>>(
            _options.DefaultHeaders.Count + request.Headers.Count + 1);

        // Request headers are collected first so they can suppress a default of the same name.
        var overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, _) in request.Headers)
            overridden.Add(name);

        foreach (var (name, value) in _options.DefaultHeaders)
        {
            if (!overridden.Contains(name))
                headers.Add(new KeyValuePair<string, string>(name, value));
        }

        foreach (var (name, value) in request.Headers)
        {
            // A null header value means "do not send this one", which is how a method opts out of
            // a client-wide default. Sending it empty instead would be a different request.
            if (value is not null)
                headers.Add(new KeyValuePair<string, string>(name, value));
        }

        if (_options.SendAcceptHeader && !overridden.Contains("Accept")
                                      && !_options.DefaultHeaders.ContainsKey("Accept"))
        {
            headers.Add(new KeyValuePair<string, string>("Accept", _serializer.ContentType));
        }

        return headers;
    }

    private async Task<T?> DeserializeAsync<T>(TransportResponse response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
            return default;

        // 204, and a 200 with an empty body, both mean there is nothing to deserialize. Handing an
        // empty stream to a JSON reader would fail with a parse error that says nothing about the
        // actual situation.
        if (response.StatusCode == 204)
            return default;

        var value = await _serializer
            .DeserializeAsync<T>(response.Content, cancellationToken)
            .ConfigureAwait(false);

        return value;
    }

    private async Task<HttpApiException> CreateApiExceptionAsync(
        ClientRequest request,
        Exchange exchange,
        CancellationToken cancellationToken)
    {
        var content = await ReadErrorContentAsync(exchange.Transport, cancellationToken).ConfigureAwait(false);

        var message = $"{request.Method.ToString().ToUpperInvariant()} {exchange.RequestUri} "
                      + $"returned {exchange.Transport.StatusCode}"
                      + (exchange.Transport.ReasonPhrase is { Length: > 0 } reason ? $" {reason}" : string.Empty)
                      + $" (transport: {exchange.TransportName}).";

        return new HttpApiException(message)
        {
            StatusCode = exchange.Transport.StatusCode,
            ReasonPhrase = exchange.Transport.ReasonPhrase,
            ResponseContent = content,
            ResponseHeaders = exchange.Transport.Headers,
            Method = request.Method,
            RequestUri = exchange.RequestUri,
            TransportName = exchange.TransportName
        };
    }

    private async Task<string?> ReadErrorContentAsync(
        TransportResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
            return null;

        var limit = _options.MaxErrorContentLength;
        if (limit <= 0)
            return null;

        try
        {
            var buffer = new byte[limit];
            var read = 0;

            while (read < limit)
            {
                var chunk = await response.Content
                    .ReadAsync(buffer.AsMemory(read, limit - read), cancellationToken)
                    .ConfigureAwait(false);

                if (chunk == 0)
                    break;

                read += chunk;
            }

            return read == 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException
                                              or NotSupportedException)
        {
            // The status code is the thing the caller needs; a body we could not read is not worth
            // replacing it with an exception about the read.
            return null;
        }
    }

    /// <summary>A response together with the detail needed to describe where it came from.</summary>
    private readonly struct Exchange(TransportResponse transport, string transportName, Uri requestUri)
        : IDisposable
    {
        internal TransportResponse Transport { get; } = transport;

        internal string TransportName { get; } = transportName;

        internal Uri RequestUri { get; } = requestUri;

        public void Dispose() => Transport.Dispose();
    }
}
