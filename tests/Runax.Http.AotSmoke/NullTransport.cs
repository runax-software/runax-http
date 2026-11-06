using Runax.Http;
using Runax.Http.Transports;

/// <summary>
/// Stands in for a real transport. The point is to run the abstractions through AOT, not to make
/// a network call, so this answers without one.
/// </summary>
internal sealed class NullTransport : IHttpTransport
{
    public string Name => "null";

    public TransportCapabilities Capabilities =>
        TransportCapabilities.PerRequestTimeout | TransportCapabilities.Multipart;

    public Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = new TransportResponse(201)
        {
            ReasonPhrase = "Created",
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")],
            Content = new MemoryStream("""{"number":1}"""u8.ToArray()),
            FinalUri = request.RequestUri
        };

        return Task.FromResult(response);
    }
}
