# Capabilities

HTTP libraries do not agree. One streams request bodies, another buffers them. One takes a proxy
per request, another only per client. One aborts mid-flight on cancellation, another checks the
token between steps.

A library that hides those differences is lying to you somewhere. `TransportCapabilities` makes
each one answerable.

## The flags

| Capability | A transport has it when it can… |
| --- | --- |
| `RequestStreaming` | send a stream body without buffering it first |
| `ResponseStreaming` | hand back a body before it has been fully received |
| `PerRequestProxy` | honour a proxy per request rather than per client |
| `PerRequestTimeout` | honour a timeout per request rather than per client |
| `Multipart` | send `multipart/form-data` |
| `AutomaticDecompression` | decompress gzip/deflate/brotli transparently |
| `AutomaticRedirect` | follow 3xx and report the final address |
| `Cookies` | keep cookies across requests through the same instance |
| `Http2`, `Http3` | negotiate that version |
| `CancellationMidFlight` | abort an in-flight request, not just between operations |

That last one is why the flag exists at all: a synchronous HTTP library wrapped in a `Task` cannot
honour a `CancellationToken` mid-request, and a client that believes otherwise will wait for a
request it thinks it cancelled.

## How the check happens

`TransportRequest.RequiredCapabilities` derives what a request needs **from the request itself**:

| Request has | Requires |
| --- | --- |
| a `StreamedContent` body | `RequestStreaming` |
| a `MultipartFormContent` body | `Multipart` |
| `Proxy` set | `PerRequestProxy` |
| `Timeout` set | `PerRequestTimeout` |
| `Version` 2 or 3 | `Http2` / `Http3` |

The executor compares that against the selected transport's `Capabilities` **before** sending, and
throws `TransportCapabilityException` naming the gap:

> Transport 'httpclient' does not support PerRequestProxy, which this request requires.

Checked once in the runtime rather than inside each transport, so every transport gets it for free
and a new one cannot forget. A capability gap is never [retried](retries.md) — waiting and asking
again will not give a transport an ability it lacks.

Response-side capabilities (`ResponseStreaming`, `Cookies`, `AutomaticDecompression`,
`AutomaticRedirect`) are properties of how a transport was configured rather than of a single
request, so they are not derived here; they are checked where that configuration is applied.

## Degrade or throw — but never pretend

A missing capability has exactly one of two outcomes, chosen per capability and documented by the
transport:

1. **Throw** `TransportCapabilityException`.
2. **Degrade** in a stated way — buffer a stream rather than streaming it, say.

What a transport must never do is quietly do something else and report success. A client that asks
for a per-request proxy and silently gets a direct connection has been failed invisibly, and the
symptom shows up somewhere else entirely — in that example, as an IP ban on the address you were
trying not to use.

That is the whole reason this model exists rather than a best-effort "we'll do what we can".

## Next

- [Writing a custom transport](writing-a-custom-transport.md) — declaring capabilities honestly
- [Transports](transports.md) — what the built-in transport declares, and the two absences
