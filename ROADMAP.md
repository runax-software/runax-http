# Runax.Http — Roadmap

Post-1.0 backlog. 1.0.0 shipped the generator and its diagnostics, the request runtime, DI wiring,
retries, run-time response dispatch, two transports with a shared conformance suite, and the
TestKit — see [CHANGELOG.md](CHANGELOG.md).

Ordered by what each item unblocks rather than by effort. Nothing here is a commitment to a date.

## Backlog

- [ ] **More transports** — Flurl, and a transport over one of the lower-level HTTP stacks
  that offer finer cookie, proxy and TLS control than `HttpClient` does. Several of those are
  synchronous and no longer maintained, which means sync-over-async and no mid-flight
  `CancellationToken` support; where that's the case it gets documented as a wart of that
  transport rather than smoothed over.
- [ ] **Benchmarks (BenchmarkDotNet)** — guard per-call overhead and allocations against
  regressions, and quantify the cost of each transport.
