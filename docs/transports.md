# Transports

A transport is the HTTP library that actually sends the request. Runax.Http treats it as a
replaceable component, which is the reason the library exists: one client interface, more than one
way to put it on the wire.

## Registering

`AddRunaxHttp()` registers the built-in `HttpClient` transport under the name `httpclient`. Other
transports add themselves alongside it:

```csharp
services.AddRunaxHttp()
        .UseSystemTextJson()
        .AddTransport(new MyTransport())            // an instance
        .AddTransport<MyOtherTransport>()           // resolved from the container
        .AddTransport(provider => Build(provider))  // built from the container
        .UseDefaultTransport(HttpClientTransport.TransportName);
```

Two transports cannot share a name. A name selects which stack a request goes over, so a duplicate
makes that selection meaningless — and registering one under a built-in name is the easy way to do
it by accident, so it fails rather than letting one shadow the other.

`UseDefaultTransport` is only needed when more than one is registered. With a single transport that
one is the default; with several and no default set, a request that names none fails rather than
being sent over whichever happened to be registered first.

## Choosing one

Three places, in increasing precedence:

```csharp
// 1. What the interface declares — a suggestion from its author
[Transport(RestSharpTransport.TransportName)]
public interface IScrapeClient { }

// 2. What registration says — the deployment-time choice
services.AddRunaxClient<IScrapeClient>()
        .UseTransport(HttpClientTransport.TransportName);

// 3. What the call site says — the final word
using (TransportScope.Use(RestSharpTransport.TransportName))
    await client.GetAsync(url);
```

**Per call beats registration beats source**, so the person closest to the decision wins. An
interface author's `[Transport]` is a default they suggest, not a constraint on whoever consumes
the interface — and overriding it at registration is how a consumer who does not want that
transport (or its package) opts out.

`[Transport]` also works on an individual method, for the one endpoint that needs different
treatment.

### Scopes

`TransportScope.Use(name)` sets the transport for everything inside it. The value flows with the
async context, so it applies across awaits and into tasks started inside the scope; it does not
flow back out, and a nested scope restores the outer one on dispose. A concurrent caller outside
the scope is unaffected.

The alternative would be a transport parameter on every client method, which would put a transport
concern into the API contract of every interface — the thing those interfaces exist to be free of.

`TransportScope.UseDefault()` clears an ambient selection, so a client falls back to its configured
transport.

## Naming

A transport is identified by a **name**, and each transport exposes its own as a constant:

```csharp
.UseTransport(RestSharpTransport.TransportName)
```

Prefer that over a string literal. You get IntelliSense off a type you already reference, and
because it is a `const string` it also works inside the attribute:
`[Transport(HttpClientTransport.TransportName)]`.

### Why not an enum

Because an enum would have to live in `Runax.Http.Abstractions` — the package every transport
already references — so no transport package could name itself without a coordinated release of the
thing at the bottom of the dependency graph. A transport that cannot name itself is not really a
plugin.

### Why not a type

`UseTransport<RestSharpTransport>()` looks like the type-safe answer and cannot express the cases
that matter. A transport's identity is its **registration**, not its type: `HttpClientTransport`
takes a name precisely so several can be registered with different handlers, proxies or timeouts —
a proxy pool is exactly that — and a type parameter cannot tell them apart. A name can also come
from configuration, and `"Transport": "restsharp"` in `appsettings.json` has to resolve to
something.

### What a string costs, and what it doesn't

A typo cannot be a build error: what is registered is a run-time fact. It is, however, an immediate
error. Configured names are checked when transports are first resolved, naming the client and
listing what exists:

> `IScrapeClient` declares `[Transport("restsharpp")]`, but no transport is registered under that
> name. Either register it, or override it with `UseTransport(...)` when registering the client.
> Registered transports: httpclient.

Names from `[Transport]` on a *method* are checked the same way, so a typo on a rarely called
method does not lie dormant until that method finally runs. Overriding a declared name at
registration suppresses the check, since that is the escape hatch.

## The built-in transport

`HttpClientTransport`, name `httpclient`, shipped inside `Runax.Http` rather than a package of its
own — it needs nothing but the BCL, so bundling it costs a consumer nothing.

It declares what a stock handler can do, and two absences are deliberate:

- **No `PerRequestProxy`.** A proxy belongs to the handler, which every request through the client
  shares. Claiming the capability and then ignoring `TransportRequest.Proxy` would send a request
  direct that the caller asked to route. Rotating proxies with this transport means a client per
  proxy.
- **No `AutomaticDecompression`.** It is off by default on `SocketsHttpHandler`. If you enable it,
  say so:

```csharp
new HttpClientTransport(
    client,
    capabilities: HttpClientTransport.DefaultCapabilities | TransportCapabilities.AutomaticDecompression)
```

The transport is built over `IHttpClientFactory` under the named client `httpclient`, so handler
configuration, lifetime and `DelegatingHandler`s work as they normally do:

```csharp
services.AddHttpClient(HttpClientTransport.TransportName)
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        });
```

## Next

- [Capabilities](capabilities.md) — what transports disagree about, and what happens then
- [Writing a custom transport](writing-a-custom-transport.md) — the `IHttpTransport` contract
- [Architecture](architecture.md) — where the boundary is, and what stays above it
