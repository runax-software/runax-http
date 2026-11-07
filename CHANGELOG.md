# Changelog

All notable changes to Runax.Http are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
All packages in the repository are versioned together.

## [Unreleased]

### Added

- **`Runax.Http`** — the package to install: the source generator, the request runtime, the
  dependency-injection wiring, and the `HttpClient` transport.
  - **Source generator.** An annotated interface in, an implementation out, at compile time, with
    no reflection in the generated code. Supports `Task`, `Task<T>`, `Task<ApiResponse>` and
    `Task<ApiResponse<T>>`; `[Query]`, `[Path]`, `[Body]`, `[Header]` and `CancellationToken`
    parameters; `[BaseAddress]`, `[Transport]` and constant `[Header]`s on the interface or method.
    A parameter with no attribute binds to a path placeholder of the same name; anything else is an
    error rather than a guess.
  - **Fourteen diagnostics, `RXH001`–`RXH014`**, each a build error with a location and a sentence
    saying what to do: an unmatched `{placeholder}`, two `[Body]` parameters, an unbindable
    parameter, a synchronous return type, a generic interface or method, a non-absolute
    `[BaseAddress]`, two parameters bound to one placeholder. Tracked in
    `AnalyzerReleases.Unshipped.md`, because these ids reach consumers' build logs.
  - **`HttpClientTransport`**, declaring what a stock handler can actually do — notably *not*
    per-request proxies, which belong to the handler and are shared by every request through the
    client, and *not* automatic decompression, which is off by default.
  - **Transport selection** with a documented precedence: `TransportScope.Use(...)` beats
    `UseTransport(...)` at registration, which beats `[Transport]` in the source, which beats the
    only registered transport. Asking for a name nothing is registered under lists the ones that
    are, since that is almost always a typo or a missing transport package.
  - **Transport names are strings, exposed per transport as a constant** —
    `HttpClientTransport.TransportName` — rather than an enum. An enum would have to live in
    `Runax.Http.Abstractions`, so no transport package could name itself without a release of the
    one package every transport already references. A type parameter would not work either: a
    transport's identity is its registration, and several of one type can be registered under
    different names with different handlers or proxies.
  - **Configured transport names are validated when transports first resolve**, not on the request
    that asks for them. A misspelled `UseTransport(...)` or `[Transport(...)]` — including on a
    method, where a typo could otherwise lie dormant until that one method ran — fails naming the
    client and listing what is registered. Overriding a declared name at registration suppresses
    the check, which is the escape hatch for a consumer who does not want the transport an
    interface asks for.
  - **`AddRunaxHttp()` and `AddRunaxClient<T>()`**, with per-client base address, transport,
    timeout, default headers and serializer. Client lookup goes through a registry that generated
    code populates from a module initializer, so registration needs no assembly scanning and works
    under trimming and native AOT.
  - **URI composition in one place.** A request path appends to the base address's path rather than
    replacing it, path values are escaped as a single segment, query values are formatted
    invariantly, a collection expands to repeated keys, and a null is omitted rather than sent
    empty. Transports never build URLs — the difference between a transport swap being invisible
    and it quietly changing which URL gets hit.
  - **`SystemTextJsonContentSerializer`**, serializing through `JsonTypeInfo` so a source-generated
    context needs no reflection. `AddRunaxHttp()` registers no serializer by default: the
    convenient reflection-based one is one explicit call that carries a trimming warning, rather
    than a silent default that opts every application out of AOT.
- **Retries**, off by default and configurable per client with `WithRetry(3)` or
  `WithRetry(options => ...)`: attempt count, base and maximum delay, constant/linear/exponential
  backoff, jitter, `Retry-After` handling, and which status codes count. They sit above the
  transport boundary, so every transport gets the same behaviour and swapping one does not change
  retry semantics.
  - Three defaults are safety decisions rather than taste. Only idempotent methods are retried —
    a retried `POST` that timed out after the server committed it creates the thing twice. A
    request whose body is a stream is never retried, because the stream is already consumed and
    buffering it would mean holding an upload of unknown size in memory. And 500 is not in the
    retryable set: 408, 429, 502, 503 and 504 are.
  - `Retry-After` is honoured, and a value longer than `MaxRetryAfterDelay` is treated as a
    refusal to retry rather than an instruction to block for it. A capability gap is never
    retried, since waiting will not give a transport an ability it lacks.
- **`Runax.Http.Abstractions`** — the attributes a client interface is written with and the
  contracts a transport implements. Zero package dependencies on every target framework: the BCL
  and nothing else, so neither an application nor a transport author inherits anything from
  referencing it.
  - Request attributes `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]`, `[Head]`, `[Options]`,
    taking a path template with `{placeholder}` segments; binding attributes `[Query]`,
    `[Path]`, `[Body]`, `[Header]`; client attributes `[BaseAddress]`, `[Transport]`,
    `[HttpClientContract]`.
  - `IHttpTransport`, with `TransportRequest` / `TransportResponse` and a closed `RequestContent`
    hierarchy (`BinaryContent`, `StreamedContent`, `FormContent`, `MultipartFormContent`). Two
    invariants keep a transport's job to translation only: the request URI arrives absolute and
    final, with the template substituted and the query encoded, and the body arrives already
    serialized. Transports never compose URLs and never serialize.
  - `TransportCapabilities`, plus `TransportRequest.RequiredCapabilities`, which derives what a
    request needs from the request itself so the check is mechanical rather than something each
    transport has to remember to perform.
  - `IContentSerializer` — serialization sits above the transport boundary, so the same client
    interface produces and accepts the same JSON whichever transport runs it.
  - `HttpApiException` (the server answered with an error status), `TransportException` (the
    request never completed), `TransportCapabilityException` (the transport can't do what the
    request needs), and `ApiResponse` / `ApiResponse<T>` for methods where a non-success status
    is an expected outcome rather than a fault.
- Test projects for both packages (137 tests across three target frameworks) and a native-AOT smoke
  test that CI publishes and runs. The smoke test exercises a generated client resolved from a real
  container over a source-generated JSON context, so AOT support covers the generated code rather
  than only the hand-written runtime.
- Repository scaffolding: Central Package Management (`Directory.Packages.props`), shared
  build and packaging settings for `src/` and `tests/`, multi-targeting
  (`net8.0;net9.0;net10.0`), warnings-as-errors, SourceLink and deterministic builds.
- CI on push and pull request: build, unit tests on every target framework, `dotnet format`
  verification, and a native-AOT publish smoke test.
- Release publishing to NuGet via Trusted Publishing (OIDC), triggered by a published GitHub
  release and versioned from the release tag.
- Community health files: contributing guide, code of conduct, security policy, issue and pull
  request templates, CODEOWNERS and Dependabot.

[Unreleased]: https://github.com/runax-software/runax-http/commits/main
