using Runax.Http.Pipeline;
using Runax.Http.Transports;

namespace Runax.Http.AotSmoke;

/// <summary>
/// A pipeline stage, so a custom one is compiled and run ahead of time rather than only the
/// built-in ones.
/// </summary>
internal sealed class AotRecordingMiddleware : IHttpMiddleware
{
    private readonly List<string> _log;

    internal AotRecordingMiddleware(List<string> log) => _log = log;

    public int Order => HttpMiddlewareOrder.Authentication + 1;

    public async Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        // Reads what the stages outside this one did, which is the thing that would break if any of
        // them needed reflection to work.
        _log.Add($"{context.Method} {context.RequestUri.AbsolutePath} "
                 + $"auth={context.GetHeader("Authorization") is not null} "
                 + $"correlated={context.CorrelationId is not null} "
                 + $"attempt={context.Attempt}");

        return await continuation(context, cancellationToken).ConfigureAwait(false);
    }
}
