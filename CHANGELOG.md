# Changelog

All notable changes to Runax.Http are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
All packages in the repository are versioned together.

## [Unreleased]

### Added

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
- Test project for the above (53 tests across three target frameworks) and a native-AOT smoke
  test that CI publishes and runs.
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
