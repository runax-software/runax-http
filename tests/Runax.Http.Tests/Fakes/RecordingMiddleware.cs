using Runax.Http.Pipeline;
using Runax.Http.Transports;

namespace Runax.Http.Tests.Fakes;

/// <summary>
/// A stage that records that it ran, for asserting on ordering and per-attempt invocation.
/// </summary>
internal sealed class RecordingMiddleware : IHttpMiddleware
{
    private readonly List<string> _log;
    private readonly string _name;
    private readonly Action<RequestContext>? _onInvoke;

    internal RecordingMiddleware(
        List<string> log,
        string name,
        int order,
        Action<RequestContext>? onInvoke = null)
    {
        _log = log;
        _name = name;
        Order = order;
        _onInvoke = onInvoke;
    }

    public int Order { get; }

    /// <summary>How many times this stage ran, which is once per attempt rather than per call.</summary>
    internal int Invocations { get; private set; }

    public async Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        Invocations++;

        lock (_log)
            _log.Add($"{_name}:enter");

        _onInvoke?.Invoke(context);

        var response = await continuation(context, cancellationToken).ConfigureAwait(false);

        lock (_log)
            _log.Add($"{_name}:exit");

        return response;
    }
}

/// <summary>A stage that answers without calling the rest of the pipeline.</summary>
internal sealed class ShortCircuitMiddleware : IHttpMiddleware
{
    private readonly int _statusCode;

    internal ShortCircuitMiddleware(int statusCode, int order = 0)
    {
        _statusCode = statusCode;
        Order = order;
    }

    public int Order { get; }

    public Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
        => Task.FromResult(new TransportResponse(_statusCode)
        {
            Content = new MemoryStream("""{"name":"short","stars":0}"""u8.ToArray())
        });
}
