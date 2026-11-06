# Runax.Http

Declarative HTTP clients for .NET: annotate an interface, get an implementation at compile time —
over whichever HTTP library you choose, swappable at run time.

This is the package to install. It carries the source generator, the request runtime, the
dependency-injection wiring, and the `HttpClient` transport.

## Getting started

```csharp
[BaseAddress("https://api.example.com")]
public interface IRepositoryClient
{
    [Get("/repos/{owner}/{name}")]
    Task<Repository> GetAsync([Path] string owner, [Path] string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}/issues")]
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string owner,
        string name,
        [Query] string state = "open",
        [Query("per_page")] int perPage = 30,
        [Query] IEnumerable<string>? labels = null,
        CancellationToken ct = default);

    [Post("/repos/{owner}/{name}/issues")]
    Task<Issue> CreateIssueAsync(string owner, string name, [Body] NewIssue issue, CancellationToken ct = default);

    // A 404 here means "no such repository", which is an answer rather than a fault.
    [Get("/repos/{owner}/{name}")]
    Task<ApiResponse<Repository>> TryGetAsync(string owner, string name, CancellationToken ct = default);
}
```

```csharp
services.AddRunaxHttp().UseSystemTextJson();
services.AddRunaxClient<IRepositoryClient>();
```

Then inject `IRepositoryClient`. A parameter with no attribute binds to a path placeholder of the
same name; anything else needs an attribute, because guessing that an unbound parameter "must be a
query parameter" is how a secret ends up in a URL.

## Return types

| Signature | Behaviour |
| --- | --- |
| `Task` | Sends the request, ignores the body, throws `HttpApiException` on an error status |
| `Task<T>` | Deserializes the body, throws on an error status |
| `Task<ApiResponse>` | Returns the status as a value; never throws on an error status |
| `Task<ApiResponse<T>>` | As above, with the body deserialized on success |

## Choosing a transport

```csharp
services.AddRunaxHttp()
        .AddTransport(myTransport)
        .UseDefaultTransport("httpclient");

services.AddRunaxClient<IRepositoryClient>()
        .UseTransport("restsharp");      // deployment-time choice

using (TransportScope.Use("restsharp")) // per-call, no signature change
    await client.GetAsync(owner, name);
```

Precedence is **scope → registration → `[Transport]` attribute → the only registered transport**.
Per call beats registration beats what the source declared, so the person closest to the decision
wins and an interface author's `[Transport]` stays a suggestion rather than a constraint on
whoever consumes the interface.

## What the generator catches

Fourteen diagnostics, `RXH001`–`RXH014`: a `{placeholder}` with no parameter, two `[Body]`
parameters, an unbindable parameter, a synchronous return type, a generic interface or method, a
`[BaseAddress]` that isn't absolute, two parameters bound to one placeholder. Every one is a build
error with a location and a sentence saying what to do.

That list is the reason to prefer a generator over a runtime proxy. A reflection-based client finds
these on the first call that reaches them, in whatever environment gets there first.

## Native AOT and trimming

The generated code uses no reflection, and the runtime's only reflective call is a module
initializer forced during registration. The one thing that can break AOT is JSON, so the choice is
explicit:

```csharp
// Trim- and AOT-safe: a source-generated context
services.AddRunaxHttp().UseSystemTextJson(new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default
});

// Convenient, and warns, because reflection-based JSON does not survive trimming
services.AddRunaxHttp().UseSystemTextJson();
```

`AddRunaxHttp()` deliberately registers **no** serializer by default. Silently picking the
reflection-based one would opt every application out of AOT without saying so.

## Two invariants worth knowing

**The request URI is composed once, above the transport.** A request path appends to the base
address's path — base `https://host/api/v2` plus path `/users` gives `https://host/api/v2/users`,
not `https://host/users`, which is what `Uri`'s own relative resolution would produce. Path values
are escaped as a single segment, query values are formatted invariantly, and a collection becomes
one repeated key per item. Transports never build URLs, which is what makes swapping one invisible.

**Every value is formatted invariantly.** `true` not `True`, ISO-8601 for dates, `1.5` regardless
of the current culture. A decimal point that becomes a comma on one machine is a bug that only
reproduces there.

## Not implemented yet

`BodyFormat.UrlEncoded` and `BodyFormat.Multipart` report `RXH014` rather than being silently sent
as JSON. Retries, logging and auth middleware are on the [roadmap](https://github.com/runax-software/runax-http/blob/main/ROADMAP.md).

## License

[MIT](https://github.com/runax-software/runax-http/blob/main/LICENSE)
