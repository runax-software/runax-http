# Runax.Http.Transports.RestSharp

A RestSharp transport for [Runax.Http](https://github.com/runax-software/runax-http). Your client
interfaces stay exactly as they are; RestSharp carries the requests instead of `HttpClient`.

```csharp
services.AddRunaxHttp()
        .UseSystemTextJson()
        .AddRestSharpTransport();

services.AddRunaxClient<IRepositoryClient>()
        .UseTransport(RestSharpTransport.TransportName);
```

Or per call, without touching a signature:

```csharp
using (TransportScope.Use(RestSharpTransport.TransportName))
    await client.GetAsync(owner, name);
```

## What it can and cannot do

It passes the same transport conformance suite as the built-in `HttpClient` transport, and declares
a **different** capability set — which is the point of that model existing:

| Capability | RestSharp | HttpClient |
| --- | --- | --- |
| `RequestStreaming` | ✗ | ✓ |
| `ResponseStreaming` | ✗ | ✓ |
| `PerRequestTimeout` | ✓ | ✓ |
| `Multipart` | ✓ | ✓ |
| `AutomaticRedirect` | ✓ (configurable) | ✓ |
| `Cookies` | when configured | ✓ |
| `AutomaticDecompression` | when configured | when configured |
| `PerRequestProxy` | ✗ | ✗ |
| `Http2` | ✓ | ✓ |
| `CancellationMidFlight` | ✓ | ✓ |

**No streaming**, in either direction. RestSharp's `ExecuteAsync` reads the whole response into
`RawBytes` before it returns, and its body parameters hold a value rather than a stream. A client
that streams a large upload over this transport is told so — `TransportCapabilityException` before
the request is sent — rather than having it quietly buffered into memory.

**No per-request proxy.** A proxy belongs to `RestClientOptions`, so it is per client. That is the
same constraint `HttpClient` has, for the same reason, and the answer is the same: register a
transport per proxy under different names.

## Several at once

Which is the reason a transport is identified by a name rather than a type:

```csharp
services.AddRunaxHttp()
        .AddRestSharpTransport(options =>
        {
            options.Name = "restsharp-eu";
            options.Proxy = new WebProxy("http://eu.proxy:8080");
        })
        .AddRestSharpTransport(options =>
        {
            options.Name = "restsharp-us";
            options.Proxy = new WebProxy("http://us.proxy:8080");
        });
```

## Options

| Option | Default |
| --- | --- |
| `Name` | `restsharp` |
| `Proxy` | none |
| `FollowRedirects` | `true` |
| `AutomaticDecompression` | `None` |
| `CookieContainer` | none |
| `Timeout` | none (per-request timeouts still apply) |
| `ConfigureClient` | — applied last, for anything not surfaced here |

`AutomaticDecompression` and `CookieContainer` are off by default so that the declared capabilities
describe *this instance* rather than what RestSharp could in principle do. Enable either and the
matching capability appears.

Or bring your own client:

```csharp
builder.AddTransport(new RestSharpTransport(myRestClient, "restsharp", myCapabilities));
```

That constructor does not dispose the client and takes the capabilities from you, since only you
know how it was configured.

## License

[MIT](https://github.com/runax-software/runax-http/blob/main/LICENSE)
