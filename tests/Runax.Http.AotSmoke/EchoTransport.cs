using Runax.Http.Transports;

namespace Runax.Http.AotSmoke;

/// <summary>
/// Answers every request with a fixed JSON body. The point is to run generated code and the
/// serializer through the AOT compiler, not to make a network call.
/// </summary>
internal sealed class EchoTransport : IHttpTransport
{
    public string Name => "echo";

    public TransportCapabilities Capabilities =>
        TransportCapabilities.PerRequestTimeout | TransportCapabilities.Multipart;

    public Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = new TransportResponse(200)
        {
            ReasonPhrase = "OK",
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")],
            Content = new MemoryStream("""{"id":7,"name":"echo"}"""u8.ToArray()),
            FinalUri = request.RequestUri
        };

        return Task.FromResult(response);
    }
}
