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
