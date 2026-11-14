using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Runax.Http.Requests;
using Runax.Http.Retry;
using Runax.Http.Transports;

namespace Runax.Http.Proxy;

/// <summary>
/// One client method, read off its attributes once and replayed on every call.
/// </summary>
/// <remarks>
/// <para>
/// The run-time counterpart of a generated method. It builds the same <see cref="ClientRequest"/>
/// the generator would have emitted code to build, and hands it to the same executor — so
/// everything after that point (URL composition, headers, transport selection, retries, the
/// pipeline, response mapping) is shared code rather than a second implementation.
/// </para>
/// <para>
/// Everything that can be decided per method is decided in <see cref="ProxyContract"/>, when the
/// client is registered. A call does no attribute reading; it fills in arguments and dispatches.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(ProxyContract.TrimmingMessage)]
[RequiresDynamicCode(ProxyContract.AotMessage)]
internal sealed class ProxyMethod
{
    private readonly RequestMethod _method;
    private readonly string _template;
    private readonly string? _transportName;
    private readonly RetryOverride? _retry;
    private readonly KeyValuePair<string, string?>[] _constantHeaders;
    private readonly (string Name, int Index)[] _path;
    private readonly Func<object?[], IEnumerable<KeyValuePair<string, object?>>>[] _query;
    private readonly (string Name, int Index, Func<object?, string?> Format)[] _headers;
    private readonly (int Index, Func<IHttpContractExecutor, object?, RequestContent> Create)? _body;
    private readonly int _tokenIndex;
    private readonly Func<IHttpContractExecutor, ClientRequest, CancellationToken, object> _dispatch;

    internal ProxyMethod(
        RequestMethod method,
        string template,
        string? transportName,
        RetryOverride? retry,
        KeyValuePair<string, string?>[] constantHeaders,
        (string Name, int Index)[] path,
        Func<object?[], IEnumerable<KeyValuePair<string, object?>>>[] query,
        (string Name, int Index, Func<object?, string?> Format)[] headers,
        (int Index, Func<IHttpContractExecutor, object?, RequestContent> Create)? body,
        int tokenIndex,
        Func<IHttpContractExecutor, ClientRequest, CancellationToken, object> dispatch)
    {
        _method = method;
        _template = template;
        _transportName = transportName;
        _retry = retry;
        _constantHeaders = constantHeaders;
        _path = path;
        _query = query;
        _headers = headers;
        _body = body;
        _tokenIndex = tokenIndex;
        _dispatch = dispatch;
    }

    /// <summary>Builds the request from the arguments and sends it.</summary>
    /// <returns>The task the interface method returns.</returns>
    internal object Invoke(IHttpContractExecutor executor, object?[] args)
    {
        var request = new ClientRequest
        {
            Method = _method,
            Template = _template,
            PathValues = Array.ConvertAll(
                _path, path => new KeyValuePair<string, object?>(path.Name, args[path.Index])),
            QueryValues = [.. _query.SelectMany(query => query(args))],

            // Constant headers first, then parameters in declaration order: the order generated
            // code lists them in, which is the order a transport sends them in.
            Headers =
            [
                .. _constantHeaders,
                .. _headers.Select(header =>
                    new KeyValuePair<string, string?>(header.Name, header.Format(args[header.Index])))
            ],
            Content = _body is { } body ? body.Create(executor, args[body.Index]) : null,
            TransportName = _transportName,
            Retry = _retry
        };

        var token = _tokenIndex >= 0 ? (CancellationToken)args[_tokenIndex]! : default;

        return _dispatch(executor, request, token);
    }

    // The helpers below are closed over a concrete type once, when the method is planned, and
    // called through a delegate from then on. That keeps MethodInfo.Invoke — and the
    // TargetInvocationException it wraps every failure in — off the request path entirely.

    internal static Func<IHttpContractExecutor, ClientRequest, CancellationToken, object> Dispatcher(
        string helper,
        Type typeArgument)
        => Bind<Func<IHttpContractExecutor, ClientRequest, CancellationToken, object>>(helper, typeArgument);

    internal static TDelegate Bind<TDelegate>(string helper, Type typeArgument)
        where TDelegate : Delegate
        => typeof(ProxyMethod)
            .GetMethod(helper, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArgument)
            .CreateDelegate<TDelegate>();

    internal static object SendEmpty(IHttpContractExecutor executor, ClientRequest request, CancellationToken token)
        => executor.SendAsync(request, token);

    internal static object SendForApiResponse(
        IHttpContractExecutor executor,
        ClientRequest request,
        CancellationToken token)
        => executor.SendForApiResponseAsync(request, token);

    // Task<T?> is Task<T> at run time, which is exactly the type the interface method returns. The
    // delegates these are bound to return object, which a reference-typed return converts to.
    private static Task<T?> SendForContent<T>(IHttpContractExecutor executor, ClientRequest request, CancellationToken token)
        => executor.SendAsync<T>(request, token);

    private static Task<ApiResponse<T>> SendForApiResponseOf<T>(
        IHttpContractExecutor executor,
        ClientRequest request,
        CancellationToken token)
        => executor.SendForApiResponseAsync<T>(request, token);

    private static ValueTask<ApiResult> ToResult<T>(
        IHttpContractExecutor executor,
        ResponseEnvelope envelope,
        CancellationToken token)
        => executor.ToResultAsync<T>(envelope, token);

    private static RequestContent SerializeJson<T>(IHttpContractExecutor executor, object? value)
        => executor.Serializer.Serialize((T)value!);

    private static RequestContent SerializeForm<TValue>(IHttpContractExecutor executor, object? value)
        => FormBody.Create((IEnumerable<KeyValuePair<string, TValue>>)value!);

    private static IEnumerable<KeyValuePair<string, object?>> DictionaryQuery<TValue>(object? value)
        => QueryFields.From((IEnumerable<KeyValuePair<string, TValue>>?)value);

    /// <summary>
    /// Sends a request for a method carrying <see cref="ResponseAttribute"/>s, and deserializes the
    /// first declared response whose conditions hold — the loop generated code unrolls.
    /// </summary>
    internal static async Task<ApiResult> SendForResultAsync(
        IHttpContractExecutor executor,
        ClientRequest request,
        ProxyResponse[] responses,
        CancellationToken token)
    {
        var envelope = await executor.SendForEnvelopeAsync(request, token).ConfigureAwait(false);

        foreach (var response in responses)
        {
            if (response.Matches(envelope))
                return await response.ToResult(executor, envelope, token).ConfigureAwait(false);
        }

        return executor.Unmatched(envelope);
    }

    internal static Func<IHttpContractExecutor, ResponseEnvelope, CancellationToken, ValueTask<ApiResult>> ResultReader(
        Type type)
        => Bind<Func<IHttpContractExecutor, ResponseEnvelope, CancellationToken, ValueTask<ApiResult>>>(
            nameof(ToResult), type);

    internal static Func<IHttpContractExecutor, object?, RequestContent> JsonBody(Type type)
        => Bind<Func<IHttpContractExecutor, object?, RequestContent>>(nameof(SerializeJson), type);

    internal static Func<IHttpContractExecutor, object?, RequestContent> FormBodyOf(Type valueType)
        => Bind<Func<IHttpContractExecutor, object?, RequestContent>>(nameof(SerializeForm), valueType);

    internal static Func<object?, IEnumerable<KeyValuePair<string, object?>>> DictionaryQueryOf(Type valueType)
        => Bind<Func<object?, IEnumerable<KeyValuePair<string, object?>>>>(nameof(DictionaryQuery), valueType);

    internal static Func<IHttpContractExecutor, ClientRequest, CancellationToken, object> ContentDispatcher(Type type)
        => Dispatcher(nameof(SendForContent), type);

    internal static Func<IHttpContractExecutor, ClientRequest, CancellationToken, object> ApiResponseDispatcher(Type type)
        => Dispatcher(nameof(SendForApiResponseOf), type);
}

/// <summary>One <see cref="ResponseAttribute"/>, as a condition and the type it selects.</summary>
/// <remarks>Several conditions on one attribute are ANDed, as they are in generated code.</remarks>
[RequiresUnreferencedCode(ProxyContract.TrimmingMessage)]
[RequiresDynamicCode(ProxyContract.AotMessage)]
internal sealed class ProxyResponse
{
    internal ProxyResponse(ResponseAttribute declared)
    {
        StatusCode = declared.StatusCode > 0 ? declared.StatusCode : null;
        MinStatusCode = declared.MinStatusCode > 0 ? declared.MinStatusCode : null;
        MaxStatusCode = declared.MaxStatusCode > 0 ? declared.MaxStatusCode : null;
        JsonProperty = declared.WhenJsonProperty;
        JsonPropertyEquals = declared.WhenJsonPropertyEquals;
        Header = declared.WhenHeader;
        HeaderEquals = declared.WhenHeaderEquals;
        ToResult = ProxyMethod.ResultReader(declared.ResponseType);
    }

    internal int? StatusCode { get; }

    internal int? MinStatusCode { get; }

    internal int? MaxStatusCode { get; }

    internal string? JsonProperty { get; }

    internal string? JsonPropertyEquals { get; }

    internal string? Header { get; }

    internal string? HeaderEquals { get; }

    internal Func<IHttpContractExecutor, ResponseEnvelope, CancellationToken, ValueTask<ApiResult>> ToResult { get; }

    internal bool IsUnconditional => StatusCode is null && MinStatusCode is null && MaxStatusCode is null
                                     && JsonProperty is null && Header is null;

    internal bool Matches(ResponseEnvelope envelope)
    {
        if (StatusCode is { } status && !envelope.HasStatus(status))
            return false;

        if ((MinStatusCode is not null || MaxStatusCode is not null)
            && !envelope.HasStatusInRange(MinStatusCode ?? 0, MaxStatusCode ?? 999))
        {
            return false;
        }

        if (JsonProperty is { } property
            && !(JsonPropertyEquals is { } expected
                ? envelope.JsonPropertyEquals(property, expected)
                : envelope.HasJsonProperty(property)))
        {
            return false;
        }

        return Header is not { } header
               || (HeaderEquals is { } value
                   ? envelope.HeaderEquals(header, value)
                   : envelope.HasHeader(header));
    }
}
