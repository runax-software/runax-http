namespace Runax.Http.Pipeline;

/// <summary>Composes the registered stages into a single delegate.</summary>
internal static class RequestPipeline
{
    /// <summary>
    /// Wraps <paramref name="terminal"/> in each stage, outermost first.
    /// </summary>
    /// <param name="middleware">The stages, in any order.</param>
    /// <param name="terminal">What runs when every stage has called through: the transport send.</param>
    /// <returns>The composed pipeline, or <paramref name="terminal"/> itself when there are no stages.</returns>
    /// <remarks>
    /// Composed once per client rather than per request: the closures are the same every time, so
    /// rebuilding them on each call would be allocation for nothing.
    /// </remarks>
    internal static RequestPipelineStep Compose(
        IReadOnlyList<IHttpMiddleware> middleware,
        RequestPipelineStep terminal)
    {
        if (middleware.Count == 0)
            return terminal;

        // OrderBy is a stable sort, so stages sharing an Order keep the order they were registered
        // in — which is the documented tiebreak.
        var ordered = middleware.OrderBy(stage => stage.Order).ToArray();
        var next = terminal;

        // Built back to front, so the lowest Order ends up outermost and runs first.
        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var stage = ordered[i];
            var downstream = next;

            next = (context, cancellationToken) =>
                stage.InvokeAsync(context, downstream, cancellationToken);
        }

        return next;
    }
}
