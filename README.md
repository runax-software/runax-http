# Runax.Http

Declarative HTTP clients for .NET: annotate an interface, get an implementation at compile
time — over whichever HTTP library you choose, swappable at run time.

> **Status: pre-release.** This repository currently holds the build and CI scaffolding. The
> packages described below are not on NuGet yet. See [ROADMAP.md](ROADMAP.md) for what lands
> when, and [CHANGELOG.md](CHANGELOG.md) for what has already landed.

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
trimming- and AOT-safe. Mistakes (a `{placeholder}` with no matching parameter, two `[Body]`
parameters, an unregistered transport name) are build errors, not 3am production errors.

## What makes it different

Existing declarative HTTP client libraries bind you to one HTTP stack. Runax.Http treats the
HTTP library as a transport, the way a messaging library treats a broker:

```csharp
// Pick a default at registration
services.AddHttpClientContract<IRepositoryClient>()
        .UseTransport("restsharp");

// Or per instance
var client = factory.Create<IRepositoryClient>("httpclient");
```

This matters when the same interface has to be reached two different ways: a plain `HttpClient`
in your service, and something with heavier cookie, proxy or TLS control for an endpoint that
doesn't want to be talked to. Today that means two clients and two sets of models. Here it
means one interface and a different transport name.

The generated code never references a specific HTTP library. It builds a transport-neutral
request and hands it to `IHttpTransport`. Serialization, retries, logging and auth all sit
above that boundary, so they are written once rather than once per transport.

## Planned packages

| Package | What it is |
| --- | --- |
| `Runax.Http.Abstractions` | Attributes and the transport-neutral contracts. Depends on no HTTP library. |
| `Runax.Http` | The source generator, DI wiring, the request pipeline, and the `HttpClient` transport. |
| `Runax.Http.Transports.RestSharp` | RestSharp transport. |
| `Runax.Http.TestKit` | A recording in-process transport: assert what your client would have sent, no sockets. |

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
