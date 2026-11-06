# Runax.Http.Abstractions

The attributes you put on a client interface, and the contracts a transport implements. No
dependency on any HTTP library, serializer or DI container — the BCL and nothing else.

Part of [Runax.Http](https://github.com/runax-software/runax-http). You normally install
`Runax.Http` instead; this package is what you reference directly when you are **writing a
transport**, or when you want a shared contracts assembly that doesn't drag the generator and
runtime along with it.

## What's in it

**Request attributes** — `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]`, `[Head]`,
`[Options]`, taking a path template with `{placeholder}` segments.

**Binding attributes** — `[Query]`, `[Path]`, `[Body]`, `[Header]`.

**Client attributes** — `[BaseAddress]`, `[Transport]`, `[HttpClientContract]`.

**Transport contracts** (`Runax.Http.Transports`) — `IHttpTransport`, `TransportRequest`,
`TransportResponse`, `RequestContent` and its kinds, `TransportCapabilities`, `RequestProxy`.

**Serialization** (`Runax.Http.Serialization`) — `IContentSerializer`.

**Results** — `ApiResponse`, `ApiResponse<T>`, `HttpApiException`, `TransportException`,
`TransportCapabilityException`.

## The contract a transport signs

`IHttpTransport` has one method. An implementation translates a `TransportRequest` into its
library's own request, sends it, and translates the result back. That is the whole job.

What it must **not** do: serialize or deserialize, retry, log, attach credentials, apply a
redirect policy of its own, or throw on a non-success status. All of that lives above the
interface so that it is written once and behaves identically on every transport. A transport
that takes any of it on makes that behaviour vary by transport, which is the single thing this
design exists to prevent.

Two invariants make the job small:

- **`TransportRequest.RequestUri` is absolute and final.** The path template is already
  substituted and the query string already built and encoded. Transports never compose URLs —
  URL joining is where HTTP libraries disagree most, and letting each one do it would mean the
  same client hitting different URLs depending on the transport selected.
- **The body is already encoded.** It arrives as `BinaryContent` or `StreamedContent`. Forms
  and multipart stay structured, because boundary generation is genuinely native to each
  library.

## Capabilities, not pretence

HTTP libraries don't agree: some stream request bodies, some buffer them; some take a proxy per
request, some only per client; some abort mid-flight on cancellation, some don't.

Each transport declares what it has in `Capabilities`. `TransportRequest.RequiredCapabilities`
derives what a given request needs from the request itself, so the check is mechanical rather
than something each transport has to remember. A gap has exactly one of two outcomes, chosen
per capability and documented: throw `TransportCapabilityException`, or degrade in a stated way.
Never quietly do something else and report success.

## License

[MIT](https://github.com/runax-software/runax-http/blob/main/LICENSE)
