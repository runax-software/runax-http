# Runax.Http documentation

Runax.Http turns an annotated C# interface into an HTTP client at compile time, over a transport
you choose: a small core of attributes and transport-neutral contracts, a source generator, and a
separate library per HTTP stack.

New to it? Read the [root README](../README.md) for the pitch, then
[Defining a client](defining-a-client.md).

## Guides

| Guide | What it covers |
| --- | --- |
| [Defining a client](defining-a-client.md) | The request attributes, path templates and placeholders, parameter binding, value formatting, bodies, headers, and how a URL is composed |
| [Responses and errors](responses.md) | The four return types and what each does with an error status; the exception types; `[Response]` and `ApiResult` for an API with more than one response shape — including one that always answers 200 |
| [Transports](transports.md) | Registering transports, the three ways to select one and their precedence, why a name rather than an enum or a type, and what the built-in `HttpClient` transport declares |
| [Capabilities](capabilities.md) | What HTTP libraries disagree about, how a request's requirements are derived, and the rule that a transport may throw or degrade but never pretend |
| [Retries](retries.md) | Turning them on, the backoff curve, `Retry-After`, and the three defaults that are safety decisions rather than taste |
| [Serialization](serialization.md) | `IContentSerializer`, why no serializer is registered by default, source-generated JSON, per-client overrides |
| [Configuration](configuration.md) | Every option on both builders, ordering guarantees, reading from `IConfiguration`, and using the runtime without a container |

## Reference

| Page | What it covers |
| --- | --- |
| [Diagnostics](diagnostics.md) | Every `RXH` code — what triggers it, why it is an error, and the fix |
| [Architecture](architecture.md) | Package layering, the transport boundary, request flow, the two invariants, and the design rules |
| [Writing a custom transport](writing-a-custom-transport.md) | The `IHttpTransport` contract, what belongs above it, declaring capabilities honestly, disposal, packaging |
| [Native AOT & trimming](aot.md) | What is already reflection-free, the one thing that is not, a working setup, and how it is verified |

## Per-package READMEs

- [Runax.Http](../src/Runax.Http/README.md) — the package to install
- [Runax.Http.Abstractions](../src/Runax.Http.Abstractions/README.md) — attributes and contracts,
  for transport authors and shared contract assemblies
- [Runax.Http.Transports.RestSharp](../src/Runax.Http.Transports.RestSharp/README.md) — the
  RestSharp transport, and a capability table showing where it differs from `HttpClient`

## Not written yet

Pages arrive with the features they describe, so there is nothing here that documents something you
cannot use. Still to come, tracked on the [roadmap](../ROADMAP.md):

- **Testing** — waiting on `Runax.Http.TestKit`
- **Observability** — waiting on the tracing and metrics work
