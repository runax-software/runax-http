# Changelog

All notable changes to Runax.Http are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
All packages in the repository are versioned together.

## [Unreleased]

### Added

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
