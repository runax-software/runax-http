# Runax.Http — Roadmap

The path to 1.0, then the backlog behind it. Ordered by what each item unblocks rather than by
effort. Nothing here is a commitment to a date.

## Milestone 0 — Scaffolding

- [x] Repository, build and CI infrastructure: Central Package Management, multi-targeting
  (`net8.0;net9.0;net10.0`), warnings-as-errors, SourceLink, Trusted Publishing to NuGet.
- [ ] `Runax.` ID prefix reservation on nuget.org (the existing `Runax.*` packages are
  unreserved, so the prefix is squattable and ships without an owner checkmark).

## Milestone 1 — The core loop

- [ ] **`Runax.Http.Abstractions`** — the request attributes (`[Get]`/`[Post]`/…, `[Query]`,
  `[Path]`, `[Body]`, `[Header]`) and the transport-neutral contracts (`IHttpTransport`,
  request/response models, `IContentSerializer`, the unified exception type).
- [ ] **Source generator** — annotated interface in, implementation out. Incremental, cached
  per interface, emitting no reflection. Targets Roslyn 4.8 so the .NET 8 SDK can load it.
- [ ] **Generator diagnostics** — every binding mistake is a build error with a code and a
  message: unmatched `{placeholder}`, duplicate `[Body]`, unsupported return type, `[Body]` on
  a GET. This is the main reason to prefer a generator over a runtime proxy, so it gets
  first-class treatment rather than being bolted on later.
- [ ] **`HttpClient` transport** — the default, in `Runax.Http`, against the BCL only.
- [ ] **DI registration** — `AddHttpClientContract<T>()`, named transport selection,
  integration with `IHttpClientFactory` for the `HttpClient` transport.

## Milestone 2 — Swapping, for real

- [ ] **Transport resolution** — a default from `[Transport]`, overridden at registration,
  overridden again per call. Resolution rules settled and documented before a second transport
  exists, not after.
- [ ] **`TransportCapabilities`** — transports do not agree on streaming, HTTP/2, per-request
  proxies, cookie containers or multipart. Each transport declares what it supports, and each
  unsupported capability has one documented behaviour: throw, or degrade. Silent degradation is
  how a library like this earns a bad reputation, so the choice is explicit per capability.
- [ ] **`Runax.Http.Transports.RestSharp`** — the second transport, which is what actually
  proves the abstraction holds.
- [ ] **Transport conformance suite** — one shared test suite every transport must pass,
  running in-process against Kestrel. A new transport is "done" when it passes, not when its
  author says so.

## Milestone 3 — Production surface

- [ ] **`Runax.Http.TestKit`** — a recording transport, so consumers can assert the request
  their client would have sent without a socket.
- [ ] **Request pipeline** — retries, logging, auth and correlation as transport-agnostic
  middleware. `DelegatingHandler` only exists in `HttpClient`-land, so anything built on it
  can't be reused for the other transports; this is the replacement.
- [ ] **AOT and trimming** — source-generated JSON, an `AotSmoke` publish in CI that fails on
  any trim or AOT warning.
- [ ] **Per-package READMEs and docs** — one documentation page per concept, one README per
  shipped package.

## Backlog

- [ ] **More transports** — Flurl, and a Leaf.xNet-style transport for the
  scraping/anti-bot cases that motivated the swappable design. Leaf.xNet is sync-only and
  effectively unmaintained, so it will be sync-over-async and won't honour a
  `CancellationToken` mid-flight; that needs documenting as a wart rather than hiding.
- [ ] **OpenAPI to interface** — generate the annotated interface from an OpenAPI document,
  so the hand-written interface is a starting point rather than the only option.
- [ ] **Polly integration** — resilience policies as pipeline middleware.
- [ ] **Observability** — OpenTelemetry-ready tracing and metrics on the pipeline, with the
  active transport as a tag.
- [ ] **Benchmarks (BenchmarkDotNet)** — guard per-call overhead and allocations against
  regressions, and quantify the cost of each transport.
- [ ] **Runtime proxy fallback** — a `DispatchProxy`-based path for interfaces that aren't
  known at compile time. Not AOT-safe, so it would be opt-in and clearly marked.
