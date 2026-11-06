using Runax.Http.Transports;

namespace Runax.Http.Tests.Fakes;

/// <summary>
/// A transport that records what it was asked to send and answers from a script.
/// </summary>
/// <remarks>
/// Most of what this library does happens before a byte reaches the network — template
/// substitution, query encoding, header merging, transport selection — so the interesting
/// assertions are about the <see cref="TransportRequest"/> that came out, not about a server's
/// reply. Hence a transport rather than a stub HTTP handler: it sits exactly at the boundary the
/// design draws.
/// </remarks>
internal sealed class RecordingTransport : IHttpTransport
{
    private readonly Func<TransportRequest, TransportResponse> _respond;

    internal RecordingTransport(
        string name = "recording",
        TransportCapabilities capabilities = TransportCapabilities.None,
        Func<TransportRequest, TransportResponse>? respond = null)
    {
        Name = name;
        Capabilities = capabilities;
        _respond = respond ?? (_ => Responses.Json("{}"));
    }

    public string Name { get; }

    public TransportCapabilities Capabilities { get; }

    /// <summary>Every request this transport was handed, in order.</summary>
    internal List<TransportRequest> Requests { get; } = [];

    /// <summary>The most recent request, for the common single-call assertion.</summary>
    internal TransportRequest Last => Requests[^1];

    /// <summary>The token handed to the most recent send, so tests can prove it was threaded through.</summary>
    internal CancellationToken LastToken { get; private set; }

    public Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        LastToken = cancellationToken;

        return Task.FromResult(_respond(request));
    }
}
