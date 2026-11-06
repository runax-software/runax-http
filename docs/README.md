# Runax.Http documentation

Runax.Http turns an annotated C# interface into an HTTP client at compile time, over a
transport you choose: a small core of attributes and transport-neutral contracts, a source
generator, and a separate library per HTTP stack.

New to the library? Read the [root README](../README.md) first.

> **These pages are being written as the packages land.** The table below is the planned
> structure, not a set of finished documents — see [ROADMAP.md](../ROADMAP.md) for ordering.
> Anything not yet linked does not exist yet.

## Concepts & guides

| Guide | What it covers |
| --- | --- |
| Defining a client | The request attributes, path templates and placeholders, parameter binding (`[Query]`, `[Path]`, `[Body]`, `[Header]`), supported return types |
| Transports | Choosing a transport at registration, per instance and per call; resolution precedence; what each shipped transport supports |
| Capabilities | `TransportCapabilities`, and what happens when a client asks for something its transport can't do |
| Serialization | `IContentSerializer`, System.Text.Json defaults, source-generated contexts, per-client overrides |
| The pipeline | Retries, logging, auth and correlation as transport-agnostic middleware |
| Error handling | The unified exception type, status-code handling, reading a response without throwing |
| Testing | The recording transport in `Runax.Http.TestKit`: asserting the request your client would have sent, without a socket |
| Diagnostics | Every generator diagnostic, what triggers it, and how to fix it |

## Reference

| Page | What it covers |
| --- | --- |
| Architecture | Package layering, the transport boundary, request flow, design rules |
| Writing a custom transport | The `IHttpTransport` SPI, the conformance suite, registration conventions |
| Native AOT & trimming | Supported packages, source-generated JSON, how support is verified |
| Configuration | Every registration option, scoping and fallback rules |

## Per-package READMEs

Each shipped package documents its own surface. Those READMEs are also what NuGet shows on the
package page, so they are written for someone who arrived there without reading this repo.
