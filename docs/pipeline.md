# The request pipeline

Logging, authentication and correlation, as middleware that works on every transport.

`DelegatingHandler` is the obvious place to put all three, and it only exists in `HttpClient`-land —
so anything built on it cannot be reused for RestSharp or anything else. This is the replacement,
and it sits above `IHttpTransport` for the same reason serialization, retries and the capability
check do: written once, identical whichever transport is underneath.

## Turning it on

Each stage is independently opt-in:

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .UseCorrelation()
        .UseLogging()
        .UseBearerToken(() => tokenCache.Current)
        .WithRetry(3);
```

Nothing is on by default. A client with no stages pays nothing — the pipeline *is* the transport
call.

## Where it sits relative to retries

**The pipeline runs inside the retry loop: once per attempt, not once per call.**

That matters, and it is not an arbitrary choice. Consider what each position gives you:

| | Outside the loop | Inside the loop |
| --- | --- | --- |
| A token refreshed after a 401 | stale on every retry ✗ | re-read per attempt ✓ |
| A correlation id | stable ✓ | regenerated per attempt ✗ |
| A log line per attempt | one line for three tries ✗ | one line each ✓ |

Neither position is right on its own. So the pipeline runs per attempt, and the
[`RequestContext`](../src/Runax.Http/Pipeline/RequestContext.cs) is created **once per call** and
reused for every attempt. Anything that must stay constant lives on the context rather than in the
stage:

```csharp
// CorrelationMiddleware, in full. ??= is the whole trick.
context.CorrelationId ??= Activity.Current?.TraceId.ToHexString() ?? NewTraceId();
```

`context.Attempt` tells the attempts apart — so one call produces one correlation id and a log line
per try, which is exactly what you want when reading back why something eventually failed.

## Ordering

Explicit, from `IHttpMiddleware.Order`. Lower runs first, meaning further from the wire.

| Stage | Order | Why there |
| --- | --- | --- |
| Correlation | 100 | Outermost, so every stage inside — and every log line — sees the same id |
| Logging | 200 | Inside correlation so it can report the id; outside auth so its elapsed time includes fetching a token, which is latency the caller really experiences |
| Authentication | 300 | Innermost, so the token is attached as late as possible |

Order comes from the stage, not from where `Use…()` sits in the chain, so reordering a fluent chain
cannot silently change behaviour. Stages sharing an `Order` keep registration order.

The gaps leave room to slot your own between any two:

```csharp
.Use(new MyMiddleware())   // Order => HttpMiddlewareOrder.Logging + 50
```

## Logging

```csharp
.UseLogging(options =>
{
    options.ResponseLevel = LogLevel.Information;
    options.ErrorStatusLevel = LogLevel.Warning;
    options.SlowRequestThreshold = TimeSpan.FromSeconds(2);
    options.LoggedHeaders.Add("X-Request-Id");
})
```

Each attempt gets a line with the method, address, status, elapsed time, transport name, attempt
number and correlation id. The `ILogger` is categorised by the contract's full type name, so a log
filter can turn one client up without turning the whole application up.

### What it will not log, and why that is not configurable

**Bodies are never logged.** There is no option. A body is the most likely place for a password, a
token or someone's personal data; application logs are routinely shipped to a third-party
aggregator and kept for months; and a switch like that gets turned on during an incident and left
on. If you need to see a body, a debugger or a local proxy shows it to you without writing it down
somewhere permanent.

**Header values are off by default** — `LoggedHeaders` is an allow-list, empty to begin with. A
deny-list protects the headers someone thought of, and the one that leaks is always the one nobody
thought of.

**Four headers are never logged, even when allow-listed**: `Authorization`,
`Proxy-Authorization`, `Cookie` and `Set-Cookie`. Name one and you get `Authorization: ***`, which
answers "was authentication even attached?" without carrying the credential. This cannot be turned
off, because an option that can print a bearer token into a log aggregator would eventually be
used.

**The query string is logged**, because a path without it rarely identifies what was asked for. Set
`LogQueryString = false` if your API takes a credential as a query parameter — though
[`RXH005`](diagnostics.md#rxh005) exists partly to stop that happening by accident.

A cancellation is logged at the request level rather than as an error: a cancelled shutdown
otherwise fills the log with errors nobody needs to act on.

## Authentication

```csharp
.UseBearerToken(() => tokenCache.Current)                       // sync
.UseBearerToken(async ct => await tokenCache.GetAsync(ct))      // async
.UseBearerToken(myProvider)                                     // IAccessTokenProvider
.UseBearerToken(myProvider, "X-Api-Key", scheme: null)          // value alone, no "Bearer "
```

The provider is asked **on every attempt**, so a token that expired between a failure and its retry
is refreshed without the retry policy knowing anything about authentication. The header is replaced
rather than appended, so a retry does not arrive carrying both the stale credential and the fresh
one.

Returning `null` sends the request unauthenticated and clears any header a previous attempt set.

Because it runs per attempt, a provider should be a cache read rather than a network call. Caching
is the provider's job — `IAccessTokenProvider` returns `ValueTask<string?>` precisely so a cache hit
costs no allocation.

## Correlation

```csharp
.UseCorrelation(options =>
{
    options.HeaderName = "X-Request-Id";
    options.PropagateTraceContext = true;
    options.Overwrite = false;
})
```

Each call gets an id, stable across retries, sent as `X-Correlation-ID` by default.

When an `Activity` is running — ASP.NET Core starts one per inbound request — its trace id is used
and W3C `traceparent` (plus `tracestate`) is propagated, so the outgoing call joins the trace
already in progress rather than starting an unconnected one. Otherwise an id is generated, shaped
like a W3C trace id so both sources look alike in a log.

A header the caller already set is left alone. A value set explicitly on the method or at
registration was set on purpose, and a stage that overrode it would make the explicit value the one
thing you cannot rely on. `Overwrite = true` if you want the opposite.

This propagates identity; it does not create spans. Tracing and metrics are the separate
[observability](../ROADMAP.md) item.

## Writing your own

```csharp
public sealed class ThrottleMiddleware : IHttpMiddleware
{
    public int Order => HttpMiddlewareOrder.Correlation - 10;   // outermost

    public async Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        await _limiter.WaitAsync(cancellationToken);

        try
        {
            return await continuation(context, cancellationToken);
        }
        finally
        {
            _limiter.Release();
        }
    }
}
```

Register with `.Use(new ThrottleMiddleware())`, or `.Use(provider => …)` when it needs something
from the container.

Three things to know:

- **Headers are the only part of the request a stage may change.** `RequiredCapabilities` is derived
  from the body, proxy, timeout and HTTP version, and the capability check runs *before* the
  pipeline — so letting a stage alter one of those would let it introduce a requirement nothing had
  checked. If you need that, the capability check has to move, and that is a design change rather
  than a configuration one.
- **Not calling `continuation` short-circuits the request**, which is how a cache or a circuit
  breaker would work.
- **One instance serves every call**, so implementations must be thread-safe. Per-call state goes on
  `context.Items`; `context.Attempt` tells you which try you are on.

## Next

- [Retries](retries.md) — what the pipeline runs inside
- [Capabilities](capabilities.md) — why a stage may only touch headers
- [Architecture](architecture.md) — the boundary all of this sits above
