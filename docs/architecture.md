# Architecture

One boundary, drawn in one place, with almost everything above it.

## Packages

```
Runax.Http.Abstractions        attributes + transport contracts. BCL only, zero dependencies.
        ▲                      Referenced by applications AND by transport authors.
        │
Runax.Http                     source generator (as an analyzer) + runtime + DI + HttpClient transport
        ▲
Runax.Http.Transports.<Name>   one per HTTP library. References Abstractions, not Runax.Http.
```

`Runax.Http.Abstractions` has **no** package dependencies on any target framework. It is what both
an application and a third-party transport reference, so anything added there is inherited by
everyone.

A transport references `Abstractions` only. It has no business knowing about the generator, the DI
wiring or the executor — and keeping the reference one-way is what makes that structural rather
than a convention.

## The boundary

`IHttpTransport` has one method. An implementation translates a `TransportRequest` into its
library's own request, sends it, and translates the result back. That is the entire job.

What lives **above** it, written once and identical on every transport:

- URL composition
- serialization and deserialization
- header merging
- retries
- the capability check
- status-code interpretation and exception mapping
- response-type dispatch

A transport that takes on any of it makes that behaviour vary by transport, which is the single
thing this design exists to prevent.

## Request flow

```
IRepositoryClient.GetAsync(owner, name, ct)          ← your call
  │
  ├─ generated client                                 ← 3–4 lines: build a ClientRequest
  │     ClientRequest { Method, Template, PathValues, QueryValues, Headers, Content }
  │
  ├─ HttpContractExecutor
  │     resolve transport        (scope → registration → [Transport] → default)
  │     compose the URI          (template + path values + query, escaped)
  │     merge headers            (client defaults ← method ← parameters, + Accept)
  │     check capabilities       → TransportCapabilityException
  │     retry loop               → backoff, Retry-After
  │        │
  │        └─ IHttpTransport.SendAsync(TransportRequest)   ← the boundary
  │                 HttpClient / RestSharp / …
  │
  └─ map the response            → T | ApiResponse<T> | ApiResult | HttpApiException
```

## Why the generated code is thin

A generated method builds a `ClientRequest` and hands it to the executor. Nothing else.

Logic in a generator is logic you debug by reading generated output, and fix by shipping a new
compiler package to everyone who installed it. Logic in the runtime is a normal library: readable,
debuggable, unit-testable, and fixable in a patch release that does not require a rebuild.

So the generator's job is to decide things at compile time — which binding each parameter has, which
type each response branch deserializes to, which diagnostics to raise — and emit the smallest
possible amount of code that records those decisions.

## The two invariants

Everything else follows from these.

**The request URI arrives absolute and final.** The path template is already substituted and the
query string already built and encoded. Transports never compose URLs.

URL joining is where HTTP libraries disagree most — whether a leading slash on the path replaces the
base path, whether a trailing slash on the base address survives, how a query value is escaped.
Left to each transport, the same client interface would hit different URLs depending on which one
was selected. That would make a transport swap observable, and a transport swap being *unobservable*
is the entire value proposition.

**The body arrives already encoded.** An object graph has been through `IContentSerializer` and
arrives as bytes. Forms and multipart stay structured, because boundary generation and field
encoding genuinely are native to each library and handing them the parts is better than handing
them bytes.

## Capabilities instead of best effort

HTTP libraries do not agree about streaming, per-request proxies, mid-flight cancellation or HTTP
version support. Rather than paper over it, each transport declares what it has, and
`TransportRequest.RequiredCapabilities` derives what a request needs from the request itself — so
the check is mechanical rather than something each transport must remember.

A gap throws. The alternative — quietly doing something else and reporting success — is how a
client that asked for a per-request proxy gets a direct connection and an IP ban on the address it
was trying not to use. See [Capabilities](capabilities.md).

## Client lookup without reflection

Generated code registers itself with `GeneratedClientRegistry` from a `[ModuleInitializer]`, which
is how `AddRunaxClient<T>()` finds an implementation without the application naming it and without
scanning assemblies. The factory is generated code — a `new` expression — so this works under
trimming and native AOT.

A module initializer runs before the first field access or method call in its module, and
`typeof(T)` is neither, so registration forces the module constructor explicitly. That is the
runtime's only reflective call.

The generator also emits what the interface declared — base address, transport name, constant
headers — rather than having registration read the attributes back off the type. Attribute
reflection at startup is exactly the kind of thing trimming removes.

## Design rules

For anyone changing this:

1. Nothing that is not literally sending bytes goes in a transport.
2. No stack-specific type (`RestClient`, `HttpRequestMessage`) crosses `IHttpTransport`.
3. If a transport genuinely cannot express something, that is a `TransportCapabilities` flag, not a
   leak and not a silent degradation.
4. Prefer a build error to a runtime one. If the generator can catch it, it should.
5. Prefer a startup error to a first-request one. If it can only be known at run time, know it as
   early as possible.

## Next

- [Writing a custom transport](writing-a-custom-transport.md)
- [Native AOT and trimming](aot.md)
