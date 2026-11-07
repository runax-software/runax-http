# Contributing to Runax.Http

Thanks for your interest in contributing! Runax.Http turns an annotated C# interface into an
HTTP client at compile time, over a transport you choose: a small core of attributes and
transport-neutral contracts, a source generator, and a separate library per HTTP stack. Here's
how to get started.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the exact version is pinned in `global.json`) —
  it builds every target framework on its own
- The [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) and
  [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) runtimes, to run the test
  assemblies for those frameworks (see [Target frameworks](#target-frameworks)). Installing
  those SDKs gets you the runtimes too.
- A code editor (Rider, Visual Studio, or VS Code with the C# Dev Kit)

## Getting started

1. Fork the repository
2. Clone your fork
    ```bash
    git clone https://github.com/<your-username>/runax-http.git
    cd runax-http
    ```
3. Restore and build
    ```bash
    dotnet restore
    dotnet build
    ```
4. Run the tests
    ```bash
    dotnet test
    ```

## Project structure

```
src/
  Directory.Build.props              # Shared build/packaging settings for shippable libraries
  Runax.Http.Abstractions/           # Attributes + transport-neutral contracts. No HTTP library.
  Runax.Http/                        # Source generator, DI wiring, pipeline, HttpClient transport
  Runax.Http.Transports.<Name>/      # One library per HTTP stack (e.g. Runax.Http.Transports.RestSharp)
  Runax.Http.TestKit/                # Recording transport for consumers' tests
tests/
  Directory.Build.props              # Shared settings for test projects (xUnit v3 on MTP)
  Compat/                            # Shims compiled into the test projects for net8.0 only
Directory.Packages.props             # Central Package Management — all NuGet versions live here
global.json                          # Pins the .NET SDK and selects the test runner
nuget.config                         # Package sources
```

## The one rule

**Generated code never references a specific HTTP library.** It builds a transport-neutral
request and hands it to `IHttpTransport`. Everything that isn't literally sending bytes —
serialization, retries, logging, auth, correlation — belongs above that boundary, so it is
written once instead of once per transport.

A change that leaks `RestClient`, `HttpRequestMessage` or any other stack-specific type past
that boundary, or that puts behaviour inside a transport which every transport would need,
will be sent back. If a transport genuinely can't express something, that's a
`TransportCapabilities` flag, not a leak.

## Target frameworks

Everything under `src/` and `tests/` multi-targets `net8.0;net9.0;net10.0`, set once in the two
`Directory.Build.props` files. The source generator is the exception: it targets
`netstandard2.0`, because that's what loads inside the compiler. A few consequences worth knowing:

- **Code has to compile against the oldest framework.** net8.0 is the floor, so an API that
  arrived later needs an `#if NET9_0_OR_GREATER` (or an equivalent that works everywhere).
  `dotnet build` catches this: it builds all three.
- **`dotnet test` runs every framework.** One test assembly per framework, so a run of the
  tests is three times the assemblies. Pass `--framework net10.0` to narrow it while iterating.
- **net8.0-only shims live in `tests/Compat`.** Those files are compiled into every test project
  for net8.0 alone; `tests/Directory.Build.props` wires them up.
- **Package versions stay single.** `Directory.Packages.props` declares one version per package
  for all three frameworks rather than a lower floor per framework — a lower floor only
  produces NU1109 downgrade errors once a transport SDK pulls a newer `Microsoft.Extensions.*`
  from its own net8.0 assets.

## Making changes

1. Create a branch from `main`
    ```bash
    git checkout -b feature/my-change
    ```
2. Make your changes
3. Ensure the project builds cleanly (`src/` treats warnings as errors)
    ```bash
    dotnet build
    ```
4. Run tests
    ```bash
    dotnet test
    ```
5. Commit using [conventional commits](https://www.conventionalcommits.org/)
    - `feat:` new feature
    - `fix:` bug fix
    - `refactor:` code change that neither fixes a bug nor adds a feature
    - `chore:` CI, deps, tooling, cleanup
    - `docs:` documentation only
6. Open a pull request against `main`

## Adding a new transport

Transports live under the `Runax.Http.Transports.*` namespace: a flat name for a library
(`Runax.Http.Transports.RestSharp`, `Runax.Http.Transports.Flurl`), with a vendor segment if one
ever needs it. The `HttpClient` transport is the exception — it lives in `Runax.Http` itself,
because it needs nothing beyond the BCL and so costs a consumer no extra dependency.

1. Create a new library under `src/` named `Runax.Http.Transports.<Name>`. It inherits all
   common settings from `src/Directory.Build.props`, so the `.csproj` only needs its references.
2. Reference the abstractions project for `IHttpTransport`, and the core for `IRunaxHttpBuilder`
   if you want to offer an `AddYourTransport()` extension:
    ```xml
    <ProjectReference Include="../Runax.Http.Abstractions/Runax.Http.Abstractions.csproj" />
    <ProjectReference Include="../Runax.Http/Runax.Http.csproj" />
    ```
   The invariant that matters is that the transport **class** only touches `Abstractions` types;
   the reference to the core exists for registration glue, not for the transport itself.
3. Implement `IHttpTransport`. Translate the neutral request into your stack's own request,
   send it, and translate the result back. Do not serialize, retry, or log in here.
4. Expose your transport's name as a `public const string TransportName`, the way
   `HttpClientTransport` does, and use it from your own registration extension. Callers then write
   `UseTransport(YourTransport.TransportName)` instead of a string literal, and because it is a
   `const` it works inside `[Transport(...)]` too. There is no central list of names to add
   yourself to — that is the point, and it is why the name is a string rather than an enum.
5. Declare what you can't do in `Capabilities`, honestly. A transport that claims streaming it
   doesn't have is worse than one that admits it.
6. Provide a dependency-injection registration extension (e.g. `AddRestSharpTransport(...)`).
7. Declare the SDK version in `Directory.Packages.props`, then reference it from your `.csproj`
   **without** a `Version` attribute (see below).
8. Add a test project under `tests/` and run the shared transport conformance suite against it.
   That suite, not a hand-written set of tests, is what makes a transport done.

## Adding a dependency

This repo uses Central Package Management, so every NuGet version is declared once.

1. Add a `<PackageVersion Include="..." Version="..." />` entry to `Directory.Packages.props`,
   in the group it fits (Core, Source generator, Transports, or Tests).
2. Reference it from the project with `<PackageReference Include="..." />` — no `Version`
   attribute. Test-only packages are already provided by `tests/Directory.Build.props`.

Be conservative with the Roslyn version. A source generator can only load in a compiler at
least as new as the Roslyn it was built against, so raising
`Microsoft.CodeAnalysis.CSharp` drops support for every SDK older than that version.

## Code style

- Follow existing conventions in the codebase; formatting is governed by `.editorconfig`
- Use `sealed` on classes that aren't designed for inheritance
- Add XML docs to public types and members
- Keep the build warning-free — `src/` compiles with warnings-as-errors
- Keep methods focused and small
- Prefer a build error over a runtime one. If the generator can catch a mistake, it should.

## Releasing

Publishing is driven by a published GitHub release, not by a push. The tag names the version:
`v0.1.0-alpha.1` publishes `0.1.0-alpha.1`.

The workflow runs in the `nuget` environment, which is where the publishing identity lives:

- **`NUGET_USER`** — an environment variable on `nuget`, the nuget.org account the Trusted
  Publishing policy belongs to. The workflow checks it is set before doing anything, because an
  empty value fails inside the login action with a message about the token rather than about the
  missing variable.
- A **Trusted Publishing policy** on nuget.org for this repository and `publish.yml`, under that
  account. No API key is stored anywhere: the workflow exchanges a GitHub OIDC token for a
  short-lived one.

The environment is also the place to add a required reviewer if you want a human gate before a
release reaches NuGet. Worth considering: a published version can be unlisted but never removed.

## Questions?

Open a [GitHub Discussion](https://github.com/runax-software/runax-http/discussions) or an issue.
