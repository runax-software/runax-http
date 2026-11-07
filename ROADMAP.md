# Runax.Http — Roadmap

Post-1.0 backlog. 1.0.0 shipped the generator and its diagnostics, the request runtime, DI wiring,
retries, run-time response dispatch, two transports with a shared conformance suite, and the
TestKit — see [CHANGELOG.md](CHANGELOG.md).

Ordered by what each item unblocks rather than by effort. Nothing here is a commitment to a date.

## Next

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
- [x] **The rest of the pipeline** — logging, auth and correlation as transport-agnostic
  middleware, above `IHttpTransport` for the same reason serialization and retries are. Runs
  inside the retry loop so a refreshed token reaches the retry, with a per-call context so a
  correlation id does not change between attempts. Ordering comes from the stage, not from
  registration order. Bodies are never logged and header values are an allow-list.
- [x] **Per-method retry** — a `[Retry]` attribute, for the one flaky endpoint on an otherwise
  healthy API. Refines the client's settings and wins on attempt count; `WithoutRetries()` on the
  client beats every declaration, so an operator keeps the last word.
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
- [ ] **Observability** — OpenTelemetry-ready tracing and metrics on the pipeline, with the
  active transport as a tag.
- [ ] **Benchmarks (BenchmarkDotNet)** — guard per-call overhead and allocations against
  regressions, and quantify the cost of each transport.
- [ ] **Runtime proxy fallback** — a `DispatchProxy`-based path for interfaces that aren't
  known at compile time. Not AOT-safe, so it would be opt-in and clearly marked.
