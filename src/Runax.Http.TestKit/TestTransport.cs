using Runax.Http.Transports;

namespace Runax.Http.Testing;

/// <summary>
/// A transport that records what it was asked to send and answers from a script, without a socket.
/// </summary>
/// <remarks>
/// <para>
/// Sits exactly at the boundary the library draws, which is what makes it the right place to test
/// a client from. Nearly everything a Runax.Http client does happens before a byte would reach the
/// network — template substitution, query encoding, header merging, serialization, transport
/// selection — so the interesting assertion is almost always about the
/// <see cref="TransportRequest"/> that came out, not about a server's reply.
/// </para>
/// <para>
/// Declares every capability by default, so a test is not failed by the harness for asking to
/// stream. Narrow it with the constructor when the point of the test <em>is</em> the capability
/// check.
/// </para>
/// </remarks>
public sealed class TestTransport : IHttpTransport
{
    private readonly Queue<Func<RecordedRequest, TestResponse>> _scripted = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _gate = new();

    private Func<RecordedRequest, TestResponse>? _fallback;

    /// <summary>Initializes a new instance of the <see cref="TestTransport"/> class.</summary>
    /// <param name="name">The name this transport is selected by.</param>
    /// <param name="capabilities">
    /// What to claim. Everything, by default, so the capability check never fails a test that was
    /// about something else.
    /// </param>
    public TestTransport(string name = "test", TransportCapabilities? capabilities = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        Capabilities = capabilities ?? Everything;
    }

    /// <summary>Every capability, which is what the test transport claims unless told otherwise.</summary>
    public static TransportCapabilities Everything { get; } =
        TransportCapabilities.RequestStreaming
        | TransportCapabilities.ResponseStreaming
        | TransportCapabilities.PerRequestProxy
        | TransportCapabilities.PerRequestTimeout
        | TransportCapabilities.Multipart
        | TransportCapabilities.AutomaticDecompression
        | TransportCapabilities.AutomaticRedirect
        | TransportCapabilities.Cookies
        | TransportCapabilities.Http2
        | TransportCapabilities.Http3
        | TransportCapabilities.CancellationMidFlight;

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TransportCapabilities Capabilities { get; }

    /// <summary>Every request this transport was handed, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return _requests.ToList();
        }
    }

    /// <summary>
    /// The most recent request, for the single-call assertion that most tests make.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing has been sent.</exception>
    public RecordedRequest Last
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count > 0
                    ? _requests[^1]
                    : throw new InvalidOperationException(
                        "No request has been sent through this transport yet. If the call under test "
                        + "threw before sending, that exception is the thing to assert on.");
            }
        }
    }

    /// <summary>The token handed to the most recent send, to prove it was threaded through.</summary>
    public CancellationToken LastToken { get; private set; }

    /// <summary>Queues one response. Queued responses are used in order, once each.</summary>
    /// <param name="response">The response.</param>
    /// <returns>This transport, for chaining.</returns>
    public TestTransport Responds(TestResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        lock (_gate)
            _scripted.Enqueue(_ => response);

        return this;
    }

    /// <summary>Queues a response computed from the request.</summary>
    /// <param name="responder">Produces the response.</param>
    /// <returns>This transport, for chaining.</returns>
    public TestTransport Responds(Func<RecordedRequest, TestResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        lock (_gate)
            _scripted.Enqueue(responder);

        return this;
    }

    /// <summary>Queues a JSON response.</summary>
    /// <param name="json">The body.</param>
    /// <param name="statusCode">The status code.</param>
    /// <returns>This transport, for chaining.</returns>
    public TestTransport RespondsJson(string json, int statusCode = 200)
        => Responds(TestResponse.Json(json, statusCode));

    /// <summary>Queues a bare status code.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>This transport, for chaining.</returns>
    public TestTransport RespondsStatus(int statusCode)
        => Responds(TestResponse.Status(statusCode));

    /// <summary>
    /// Answers every request that outlives the queue, so a test does not have to script a response
    /// per call when it only cares about one of them.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>This transport, for chaining.</returns>
    public TestTransport AlwaysResponds(TestResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        _fallback = _ => response;

        return this;
    }

    /// <inheritdoc cref="AlwaysResponds(TestResponse)" />
    /// <param name="responder">Produces the response.</param>
    public TestTransport AlwaysResponds(Func<RecordedRequest, TestResponse> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        _fallback = responder;

        return this;
    }

    /// <summary>
    /// Makes the next request fail as though it never reached a server.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <returns>This transport, for chaining.</returns>
    /// <remarks>
    /// For testing retry behaviour and the <see cref="TransportException"/> path, which a status
    /// code cannot express — the distinction between "the server said 503" and "the server was
    /// never reached" is one callers are expected to handle differently.
    /// </remarks>
    public TestTransport FailsOnce(string message = "The connection was refused.")
        => Responds(_ => throw new TransportException(message) { TransportName = Name });

    /// <inheritdoc />
    public Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recorded = new RecordedRequest(request);
        Func<RecordedRequest, TestResponse>? responder;

        lock (_gate)
        {
            _requests.Add(recorded);
            LastToken = cancellationToken;

            responder = _scripted.Count > 0 ? _scripted.Dequeue() : _fallback;
        }

        if (responder is null)
        {
            // A clear message beats a NullReferenceException or an empty 200 that makes the test
            // fail somewhere unrelated.
            throw new InvalidOperationException(
                $"The test transport has no response scripted for {recorded}. Add one with "
                + "Responds(...), or AlwaysResponds(...) to answer every request.");
        }

        return Task.FromResult(responder(recorded).ToTransportResponse());
    }
}
