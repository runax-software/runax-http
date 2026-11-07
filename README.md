# Runax.Http

Declarative HTTP clients for .NET: annotate an interface, get an implementation at compile
time — over whichever HTTP library you choose, swappable at run time.

```bash
dotnet add package Runax.Http
```

See [CHANGELOG.md](CHANGELOG.md) for what each release contains and [ROADMAP.md](ROADMAP.md) for
what comes next.

## The idea

You describe the API as a C# interface:

```csharp
[BaseAddress("https://api.example.com")]
public interface IRepositoryClient
{
    [Get("/repos/{owner}/{name}")]
    Task<Repository> GetAsync([Path] string owner, [Path] string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}/issues")]
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        [Path] string owner,
        [Path] string name,
        [Query] string state = "open",
        [Query] int perPage = 30,
        CancellationToken ct = default);

    [Post("/repos/{owner}/{name}/issues")]
    Task<Issue> CreateIssueAsync(
        [Path] string owner,
        [Path] string name,
        [Body] NewIssue issue,
        CancellationToken ct = default);
}
```

A source generator emits the implementation at compile time — no reflection, no proxy types,
trimming- and AOT-safe. Mistakes are build errors rather than 3am production errors: a
`{placeholder}` with no matching parameter, two `[Body]` parameters, an unbindable parameter, a
synchronous return type, a non-absolute `[BaseAddress]`, a query type that cannot be sent, a
response type missing from your JSON context — each with a location and a sentence saying what to
do. The full set is in [docs/diagnostics.md](docs/diagnostics.md).

A parameter with no attribute binds to a path placeholder of the same name. Anything else is an
error, because guessing that an unbound parameter "must be a query parameter" is how a secret ends
up in a URL.

## What makes it different

Existing declarative HTTP client libraries bind you to one HTTP stack. Runax.Http treats the
HTTP library as a transport, the way a messaging library treats a broker:

```csharp
services.AddRunaxHttp().UseSystemTextJson();

// Pick a transport at registration. Each transport package exposes its own name
// as a constant, so the set stays open to anyone without a central registry.
services.AddRunaxClient<IRepositoryClient>()
        .UseTransport(RestSharpTransport.TransportName);

// Or per call, without changing a single method signature
using (TransportScope.Use(HttpClientTransport.TransportName))
    await client.GetAsync(owner, name);
```

Precedence is **scope → registration → `[Transport]` attribute → the only registered transport**,
so the person closest to the decision wins and an interface author's `[Transport]` stays a
suggestion rather than a constraint on whoever consumes it.

This matters when the same interface has to be reached two different ways: a plain `HttpClient`
in your service, and something with heavier cookie, proxy or TLS control for an endpoint that
doesn't want to be talked to. Today that means two clients and two sets of models. Here it
means one interface and a different transport name.

An API that answers with a different shape per status code — or one that always answers 200 and
puts the failure in the body — is described with `[Response]` and returns `ApiResult`, which
discriminates on the status, a JSON property or a header. Retries are built in and off by default:
`WithRetry(3)`.

The generated code never references a specific HTTP library. It builds a transport-neutral
request and hands it to `IHttpTransport`. Serialization, retries, logging, auth and telemetry all
sit above that boundary, so they are written once rather than once per transport — and the
[observability](docs/observability.md) that `UseTelemetry()` emits follows the OpenTelemetry HTTP
conventions, tags whichever transport answered, and takes no dependency on OpenTelemetry to do it.

## Packages

| Package | What it is |
| --- | --- |
| **`Runax.Http`** | The one to install: the source generator, DI wiring, the request runtime, and the `HttpClient` transport. |
| `Runax.Http.Abstractions` | Attributes and the transport-neutral contracts. Zero dependencies — the BCL and nothing else. Reference it directly when writing a transport. |
| `Runax.Http.Transports.RestSharp` | RestSharp transport. Passes the same conformance suite, with a different capability set. |
| `Runax.Http.TestKit` | A recording transport and a harness: assert what your client would have sent, no sockets. |

`HttpClient` ships inside `Runax.Http` rather than as a separate transport package. The rule
behind the split is that no package forces a third-party dependency on you — and `HttpClient`
is in the BCL, so bundling it costs nothing. Every transport that does carry an SDK gets its
own `Runax.Http.Transports.*` package.

## Documentation

See [docs/](docs/README.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports, transport implementations and
documentation fixes are all welcome.

## License

[MIT](LICENSE)
