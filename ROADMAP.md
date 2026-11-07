# Runax.Http — Roadmap

Post-1.0 backlog. 1.0.0 shipped the generator and its diagnostics, the request runtime, DI wiring,
retries, run-time response dispatch, two transports with a shared conformance suite, and the
TestKit — see [CHANGELOG.md](CHANGELOG.md).

Ordered by what each item unblocks rather than by effort. Nothing here is a commitment to a date.

## Milestone 0 — Scaffolding

- [x] Repository, build and CI infrastructure: Central Package Management, multi-targeting
  (`net8.0;net9.0;net10.0`), warnings-as-errors, SourceLink, Trusted Publishing to NuGet.

## Milestone 1 — The core loop

- [x] **`Runax.Http.Abstractions`** — the request attributes (`[Get]`/`[Post]`/…, `[Query]`,
  `[Path]`, `[Body]`, `[Header]`) and the transport-neutral contracts (`IHttpTransport`,
  request/response models, `RequestContent`, `TransportCapabilities`, `IContentSerializer`, the
  unified exception types, `ApiResponse<T>`). Zero package dependencies.
- [x] **Source generator** — annotated interface in, implementation out. Incremental, cached
  per interface, emitting no reflection. Targets Roslyn 4.8 so the .NET 8 SDK can load it.
- [x] **Generator diagnostics** — `RXH001`–`RXH014`, each a build error (or, for `[Body]` on a
  GET, a warning) with a location and a sentence saying what to do. This is the main reason to
  prefer a generator over a runtime proxy, so it got first-class treatment rather than being
  bolted on later.
- [x] **`HttpClient` transport** — the default, in `Runax.Http`, against the BCL only.
- [x] **DI registration** — `AddRunaxHttp()` and `AddRunaxClient<T>()`, named transport
  selection, and `IHttpClientFactory` behind the `HttpClient` transport.

## Milestone 2 — Swapping, for real

- [x] **Transport resolution** — a default from `[Transport]`, overridden at registration,
  overridden again per call by `TransportScope`. Settled and documented before a second transport
  exists, as intended.
- [x] **`TransportCapabilities`** — each transport declares what it supports, and the check is
  applied once in the runtime rather than in each transport, so a new one cannot forget it. A gap
  throws `TransportCapabilityException` before the request is sent.
- [x] **`Runax.Http.Transports.RestSharp`** — the second transport, which is what actually
  proves the abstraction holds. It declares a different capability set (no streaming in either
  direction) and passes the same suite, which is the model working rather than a compromise.
- [x] **Transport conformance suite** — one shared suite every transport must pass, against a real
  Kestrel server on a loopback port. A new transport is "done" when it passes, not when its author
  says so. It found two bugs in the RestSharp transport on its first run.

## Milestone 2 remainder

- [x] **Run-time response dispatch** — `[Response]` plus `ApiResult`, for an API with a different
  shape per status code and for one that always answers 200 and puts the outcome in the body.
  Discriminates on status, status range, a top-level JSON property (presence or value), or a
  header. Each branch names a concrete type, so the decision is dynamic and the deserialization
  is not.

- [ ] **`BodyFormat.UrlEncoded` and `BodyFormat.Multipart`** — currently `RXH014`, a build error
  saying they are not emitted yet, rather than silently sent as JSON.

## Milestone 3 — Production surface

- [x] **Generator tests** — a `CSharpGeneratorDriver` harness, one test per diagnostic, and
  assertions that the emitted code compiles cleanly, keeps nullable annotations, registers itself
  and contains nothing reflective.
- [x] **`Runax.Http.TestKit`** — a recording transport and a harness that stands up the real
  generated client and runtime over it, so consumers assert on the request their client built
  rather than on a mock that skips the code under test.
- [x] **Retries** — configurable per client and off by default, above the transport boundary so
  every transport gets the same behaviour. Exponential/linear/constant backoff with jitter,
  `Retry-After` honoured, idempotent methods only unless asked otherwise, and never for a body
  that cannot be replayed.
- [ ] **The rest of the pipeline** — logging, auth and correlation as transport-agnostic
  middleware. `DelegatingHandler` only exists in `HttpClient`-land, so anything built on it
  can't be reused for the other transports; this is the replacement.
- [ ] **Per-method retry** — a `[Retry]` attribute, for the one flaky endpoint on an otherwise
  healthy API. Registration-level configuration covers the common case today.
- [x] **AOT and trimming** — source-generated JSON, and an `AotSmoke` publish in CI that fails on
  any trim or AOT warning and then runs the binary.
- [x] **Per-package READMEs and docs** — one documentation page per concept, one README per
  shipped package.

## Backlog

- [ ] **More transports** — Flurl, and a transport over one of the lower-level HTTP stacks
  that offer finer cookie, proxy and TLS control than `HttpClient` does. Several of those are
  synchronous and no longer maintained, which means sync-over-async and no mid-flight
  `CancellationToken` support; where that's the case it gets documented as a wart of that
  transport rather than smoothed over.
- [ ] **OpenAPI to interface** — generate the annotated interface from an OpenAPI document,
  so the hand-written interface is a starting point rather than the only option.
- [ ] **Polly integration** — resilience policies as pipeline middleware.
- [ ] **Observability** — OpenTelemetry-ready tracing and metrics on the pipeline, with the
  active transport as a tag.
- [ ] **Benchmarks (BenchmarkDotNet)** — guard per-call overhead and allocations against
  regressions, and quantify the cost of each transport.
- [ ] **Runtime proxy fallback** — a `DispatchProxy`-based path for interfaces that aren't
  known at compile time. Not AOT-safe, so it would be opt-in and clearly marked.
