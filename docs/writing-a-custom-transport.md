# Writing a custom transport

A transport is one class with one method. The contract is deliberately small — most of what an HTTP
client library does happens above it.

## The contract

```csharp
public interface IHttpTransport
{
    string Name { get; }
    TransportCapabilities Capabilities { get; }
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default);
}
```

Translate the request into your library's own, send it, translate the result back. That is the job.

### What not to do

Do **not** serialize or deserialize, retry, log, attach credentials, follow a redirect policy of
your own, or throw on a non-success status. All of that lives above this interface, written once, so
that it behaves identically whichever transport is underneath. A transport that takes any of it on
makes that behaviour vary by transport.

A 404 or a 500 is a **response**, not a failure. Return it. Deciding what a status code means
belongs to the caller, which can see the client interface's declared return type.

### What you are given

`TransportRequest.RequestUri` is **absolute and final** — template substituted, query built and
encoded. Do not compose URLs; see [Architecture](architecture.md#the-two-invariants) for why.

`Content` is already encoded:

| Content | Send it as |
| --- | --- |
| `BinaryContent` | the bytes, with `ContentType` |
| `StreamedContent` | the stream, without buffering (needs `RequestStreaming`) |
| `FormContent` | your library's form encoding, from the unencoded fields |
| `MultipartFormContent` | your library's multipart, generating the boundary |

The hierarchy is closed to `Runax.Http.Abstractions`, so a `switch` over those four is exhaustive
today. Throw on the default branch anyway, so that a fifth kind is a loud failure rather than a
request sent with no body.

## A skeleton

```csharp
public sealed class MyTransport : IHttpTransport
{
    public const string TransportName = "mystack";

    private readonly MyClient _client;

    public MyTransport(MyClient client) => _client = client;

    public string Name => TransportName;

    public TransportCapabilities Capabilities =>
        TransportCapabilities.PerRequestTimeout
        | TransportCapabilities.Multipart
        | TransportCapabilities.CancellationMidFlight;

    public async Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var native = Translate(request);

        try
        {
            var response = await _client.SendAsync(native, cancellationToken).ConfigureAwait(false);

            return new TransportResponse((int)response.Status, new Handle(response))
            {
                ReasonPhrase = response.Reason,
                Headers = Flatten(response.Headers),
                Content = response.Body,
                FinalUri = response.FinalUri
            };
        }
        catch (MyLibraryException exception)
        {
            // Normalized, so callers catch one type whatever transport ran the request.
            throw new TransportException(
                $"The request to {request.RequestUri} did not complete: {exception.Message}",
                exception)
            {
                TransportName = Name,
                RequestUri = request.RequestUri
            };
        }
    }
}
```

### Expose your name as a constant

```csharp
public const string TransportName = "mystack";
```

Callers then write `UseTransport(MyTransport.TransportName)` instead of a string literal, and
because it is a `const` it works inside `[Transport(...)]` too. There is no central list of names to
add yourself to — that is the point, and why the name is a string rather than an enum. See
[Transports](transports.md#naming).

### Declare capabilities honestly

This is the part worth getting right. A transport that claims streaming it does not have is worse
than one that admits it, because the failure moves somewhere unrelated.

If your library takes a proxy per client rather than per request, do **not** declare
`PerRequestProxy` — a client that asked to route a request and got a direct connection has been
failed invisibly. If it is synchronous and you are wrapping it in a `Task`, do not declare
`CancellationMidFlight`.

Where a capability is configuration-dependent, take it as a constructor parameter the way
`HttpClientTransport` does, rather than guessing.

### Disposal

`TransportResponse` takes a transport resource to dispose with it — typically your library's own
response object. Implement **both** `IDisposable` and `IAsyncDisposable` on it, so the synchronous
path is a real dispose rather than an async one drained on the calling thread.

Do not dispose the response before returning: that would close the body stream you just handed out,
and the symptom would be an empty body rather than an error.

Do not dispose a `StreamedContent` body — the caller owns it.

### Thread safety

One instance is registered as a singleton and used concurrently. If your library's client is not
thread-safe, pool or create per call.

## Registration

```csharp
public static IRunaxHttpBuilder AddMyTransport(
    this IRunaxHttpBuilder builder,
    Action<MyOptions>? configure = null)
{
    // …build the client, then:
    return builder.AddTransport(provider => new MyTransport(client));
}
```

Called alongside `AddRunaxHttp()`:

```csharp
services.AddRunaxHttp()
        .UseSystemTextJson()
        .AddMyTransport();
```

## Packaging

Name it `Runax.Http.Transports.<Name>`, reference `Runax.Http.Abstractions` (not `Runax.Http`), and
declare your SDK's version in `Directory.Packages.props` if you are contributing it here. See
[CONTRIBUTING.md](../CONTRIBUTING.md).

## Conformance

A shared test suite every transport must pass is on the [roadmap](../ROADMAP.md) and is what will
make a transport "done" rather than its author's say-so. Until it exists, the behaviours to cover
are: every method maps correctly, the URI is used as given, headers reach the right collection
(content headers included), an error status comes back as a response rather than an exception, the
body is readable after `SendAsync` returns, a connection failure becomes a `TransportException`, and
a per-request timeout is reported as a timeout rather than a cancellation.

`HttpClientTransportTests` in this repository is the current reference for that list.
