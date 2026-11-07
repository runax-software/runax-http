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
        .UseDefaultTransport(HttpClientTransport.TransportName);

services.AddRunaxClient<IRepositoryClient>()
        .UseTransport(RestSharpTransport.TransportName);   // deployment-time choice

using (TransportScope.Use(RestSharpTransport.TransportName))  // per call, no signature change
    await client.GetAsync(owner, name);
```

Precedence is **scope → registration → `[Transport]` attribute → the only registered transport**.
Per call beats registration beats what the source declared, so the person closest to the decision
wins and an interface author's `[Transport]` stays a suggestion rather than a constraint on
whoever consumes the interface.

### Naming a transport

Every transport exposes its own name as a constant — `HttpClientTransport.TransportName`,
`RestSharpTransport.TransportName` — and that is the form to prefer. You get IntelliSense off a
type you already reference, and a `const string` also works inside the attribute:

```csharp
[Transport(HttpClientTransport.TransportName)]
public interface IRepositoryClient { }
```

A transport is identified by a **name**, not a type, and deliberately not an enum:

- An enum would have to live in `Runax.Http.Abstractions`, the package every transport already
  references, so no transport package could name itself without a coordinated release of the one
  thing at the bottom of the dependency graph.
- A type parameter (`UseTransport<RestSharpTransport>()`) can't express the cases that matter.
  A transport's identity is its *registration*: `HttpClientTransport` takes a name precisely so
  several can be registered with different handlers, proxies or timeouts, and a type cannot tell
  those apart.
- A name can come from configuration. `"Transport": "restsharp"` in `appsettings.json` has to
  resolve to something, and that something is a string.

The cost of a string is that a typo cannot be a build error. It is, however, an immediate one: a
client registered with a name nothing is registered under fails when transports are first
resolved, naming the client and listing what *is* registered — rather than surfacing on whichever
request first asks for it. Names from `[Transport]` on a method are checked the same way, so a
typo on a rarely called method does not lie dormant.

## When one return type isn't enough

Two cases the table above can't express.

**A different shape per status code.** Declare one per outcome and return `ApiResult`:

```csharp
[Get("/widgets/{id}")]
[Response(200, typeof(Widget))]
[Response(404, typeof(NotFound))]
[Response(422, typeof(ValidationProblem))]
[Response(typeof(ServerError), MinStatusCode = 500, MaxStatusCode = 599)]
Task<ApiResult> GetAsync(int id, CancellationToken ct = default);
```

**An API that answers 200 whatever happened** and puts the outcome in the body. This is common, it
is not going to be fixed, and a status code cannot tell these apart — so discriminate on the body
or a header instead:

```csharp
[Get("/legacy/widgets/{id}")]
[Response(typeof(ApiError), WhenJsonProperty = "error")]
[Response(typeof(ApiError), WhenJsonProperty = "success", WhenJsonPropertyEquals = "false")]
[Response(typeof(ApiError), WhenHeader = "X-Error")]
[Response(typeof(Widget))]                                  // whatever is left
Task<ApiResult> GetLegacyAsync(int id, CancellationToken ct = default);
```

Without this, a client deserializes that error envelope into a `Widget` and hands the caller an
object with default fields — a failure that looks like success all the way to wherever the fields
get used.

Reading the result is a pattern match, which is how the static typing comes back:

```csharp
var result = await client.GetAsync(id);

return result.Value switch
{
    Widget widget       => Ok(widget),
    ValidationProblem p => BadRequest(p),
    NotFound            => NotFound(),
    _                   => Problem($"Unrecognized: {result.UnmatchedContent}")
};
```

Or `result.Require<Widget>()` at the point where you want the failure to be loud, and
`TryGet<T>` / `Is<T>` in between.

A few rules worth knowing:

- **Order decides.** The first `[Response]` whose conditions all hold wins, in the order written.
  A specificity rule would mean guessing which of two overlapping conditions the library thinks is
  narrower, and getting it wrong quietly. One with no conditions matches anything, so it goes
  last — the generator warns (`RXH018`) about anything after it.
- **Conditions on one attribute are ANDed.** `[Response(200, typeof(ApiError), WhenJsonProperty = "error")]`
  needs both.
- **Nothing is guessed.** If no declared response matches, `Matched` is false and the raw body is
  on `UnmatchedContent` — the only clue as to which `[Response]` is missing.
- **Nothing throws on this path.** A method declaring `ApiResult` has said an error status is one
  of the expected answers.
- **This path buffers the body**, because choosing the type by inspecting it requires that. The
  `Task<T>` path streams and does not pay for it.

## Retries

Off until you ask for them:

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .WithRetry(3);                   // one try, two retries

services.AddRunaxClient<IRepositoryClient>()
        .WithRetry(options =>
        {
            options.MaxAttempts = 5;
            options.Delay = TimeSpan.FromMilliseconds(200);
            options.MaxDelay = TimeSpan.FromSeconds(10);
            options.Backoff = BackoffStrategy.Exponential;   // or Linear, Constant
            options.UseJitter = true;
            options.RespectRetryAfter = true;
            options.RetryableStatusCodes.Add(500);
            options.RetryNonIdempotentMethods = true;        // read the warning below first
        });
```

Three defaults are safety decisions rather than taste, and worth knowing before you change them:

**Idempotent methods only.** `GET`, `HEAD`, `OPTIONS`, `PUT` and `DELETE` are retried; `POST` and
`PATCH` are not. A retried `POST` that timed out *after* the server committed it creates the thing
twice, and the client has no way to know which happened. Turn
`RetryNonIdempotentMethods` on only for an endpoint with an idempotency key or a genuinely
idempotent handler.

**A streamed body is never retried**, whatever the settings say. The stream was consumed by the
first attempt, so a second would send a truncated request — and buffering it instead would mean
holding an upload of unknown size in memory on the off chance of a retry.

**500 is not retryable by default.** 408, 429, 502, 503 and 504 are. A plain Internal Server Error
is usually a bug in the request or the handler, so repeating it multiplies the log noise without
changing the outcome. Add it if your server is known to use 500 for transient faults.

Beyond that: `Retry-After` is honoured when present, and a value longer than `MaxRetryAfterDelay`
is treated as a refusal to retry rather than an instruction to block for it. Jitter is on, because
without it every client that failed at the same moment retries at the same moment and a recovering
service gets hit by a synchronized wave. A capability gap is never retried — waiting will not give
a transport an ability it lacks.

Retries live above the transport boundary, so every transport gets the same behaviour and none of
them implements it. Swapping a transport does not change your retry semantics.

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
