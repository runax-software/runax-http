using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>The next stage of the pipeline, ending in the transport itself.</summary>
/// <param name="context">The request being sent.</param>
/// <param name="cancellationToken">Cancels the request.</param>
/// <returns>The response.</returns>
public delegate Task<TransportResponse> RequestPipelineStep(
    RequestContext context,
    CancellationToken cancellationToken);
