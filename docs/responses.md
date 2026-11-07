# Responses and errors

What a client method returns decides what happens to an error status. That is the whole of the
design: the signature says whether a non-success response is a fault or an answer.

## The four return types

| Signature | On success | On an error status |
| --- | --- | --- |
| `Task` | returns; body ignored | throws `HttpApiException` |
| `Task<T>` | deserializes the body | throws `HttpApiException` |
| `Task<ApiResponse>` | returns the status | returns it; `Error` is set |
| `Task<ApiResponse<T>>` | deserializes the body | returns it; `Content` null, `Error` set |

A synchronous return type is an error ([`RXH002`](diagnostics.md)). HTTP is asynchronous, so a
synchronous signature could only be implemented by blocking, which deadlocks often enough that it
is not offered.

### `Task<T>` — the common case

```csharp
[Get("/repos/{owner}/{name}")]
Task<Repository> GetAsync(string owner, string name, CancellationToken ct = default);
```

A 404 here throws. That is usually right: if you asked for a repository by name and there isn't
one, something upstream is wrong.

### `Task<ApiResponse<T>>` — when the error is an answer

```csharp
[Get("/repos/{owner}/{name}")]
Task<ApiResponse<Repository>> TryGetAsync(string owner, string name, CancellationToken ct = default);
```

```csharp
var response = await client.TryGetAsync(owner, name);

if (!response.IsSuccessStatusCode)
    return null;

return response.EnsureSuccess();
```

The cases worth this: a 404 that means "no such thing" rather than "something is broken", and a 409
or 422 whose body carries the validation detail you actually want. Both read badly as exceptions.

`EnsureSuccess()` throws the error if there is one, and throws `InvalidOperationException` if the
status succeeded with no body — returning `null!` from a non-nullable signature would push the
failure to the caller's next dereference, somewhere with no context about the request.

## Exceptions

Two types, split by what the caller can do about it:

**`HttpApiException`** — the server answered, and the answer was an error status. Carries
`StatusCode`, `ReasonPhrase`, `ResponseContent`, `ResponseHeaders`, `Method`, `RequestUri` and
`TransportName`.

**`TransportException`** — the request never completed: DNS failure, connection refused, TLS
failure, timeout. There is no status code to interpret.

The distinction is deliberate. "The server said 503" and "the server was never reached" call for
different handling — one is often worth retrying against the same host, the other may mean the host
is wrong — and every HTTP library signals it differently. Transports normalize it, so catching
these does not mean catching RestSharp's exception in one place and
`HttpRequestException` in another, and swapping a transport does not change what you catch.

Two subtypes of `TransportException`:

- **`TransportCapabilityException`** — the request needs something the transport cannot do. See
  [Capabilities](capabilities.md).
- **`UnknownTransportException`** — a transport was asked for by a name nothing is registered
  under. See [Transports](transports.md).

### Error bodies are read eagerly

`HttpApiException.ResponseContent` holds the body as text, capped at
`MaxErrorContentLength` (32 KB by default). The response is disposed before the exception reaches
you, so the body has to be read up front or it would be gone by the time anyone wanted it — and an
error body is almost always the only thing that explains the failure. The cap is there because
"unprocessable entity" occasionally comes back as a megabyte of HTML, which should not end up in a
log line.

## More than one response shape

Two cases none of the four return types can express.

### A different shape per status code

```csharp
[Get("/widgets/{id}")]
[Response(200, typeof(Widget))]
[Response(404, typeof(NotFound))]
[Response(422, typeof(ValidationProblem))]
[Response(typeof(ServerError), MinStatusCode = 500, MaxStatusCode = 599)]
Task<ApiResult> GetAsync(int id, CancellationToken ct = default);
```

### An API that answers 200 whatever happened

This is common, it is not going to be fixed, and no status code can tell these apart. So
discriminate on the body or a header instead:

```csharp
[Get("/legacy/widgets/{id}")]
[Response(typeof(ApiError), WhenJsonProperty = "error")]
[Response(typeof(ApiError), WhenJsonProperty = "success", WhenJsonPropertyEquals = "false")]
[Response(typeof(ApiError), WhenHeader = "X-Error")]
[Response(typeof(Widget))]                                   // whatever is left
Task<ApiResult> GetLegacyAsync(int id, CancellationToken ct = default);
```

Without this, the client deserializes that error envelope into a `Widget` and hands back an object
with default fields — a failure that looks like success all the way to wherever the fields get used.

### Conditions

| Property | Matches when |
| --- | --- |
| `[Response(404, …)]` | the status is exactly 404 |
| `MinStatusCode` / `MaxStatusCode` | the status falls in the inclusive range |
| `WhenJsonProperty` | the body is a JSON object with that top-level property, any value |
| `WhenJsonPropertyEquals` | …and its value is this, as text, case-sensitively |
| `WhenHeader` | the response carries that header, any value |
| `WhenHeaderEquals` | …and its value is this, case-insensitively |
| *(nothing)* | always — the fallback |

Values compare against their JSON spelling, so `WhenJsonPropertyEquals = "false"` matches
`"success": false` and `"42"` matches `"code": 42`. Only the top level is searched: a discriminator
buried in a nested object is not one anyone can rely on.

A body that is not JSON, or not valid JSON, means the condition does not match — not a parse
error. An API that answers 200 with an HTML maintenance page should fall through to the next
declaration rather than blowing up inside the condition check.

### Order decides

The first declaration whose conditions all hold wins, **in the order written**. Conditions on one
attribute are ANDed: `[Response(200, typeof(ApiError), WhenJsonProperty = "error")]` needs both.

Order rather than a specificity rule, deliberately. Specificity would mean working out which of two
overlapping conditions the library considers narrower — and being wrong about it silently. An
unconditional declaration matches everything, so it belongs last; [`RXH018`](diagnostics.md) warns
about anything placed after one.

### Reading the result

`ApiResult.Value` is typed as `object` because the whole point is that the shape is not known until
the response arrives. C# has no union type, so a pattern match is how the static typing comes back:

```csharp
var result = await client.GetAsync(id);

return result.Value switch
{
    Widget widget       => Ok(widget),
    ValidationProblem p => BadRequest(p),
    NotFound            => NotFound(),
    _                   => Problem($"Unrecognized: {result.UnmatchedContent}")
};
```

| Member | Use |
| --- | --- |
| `Value` | the deserialized body; pattern match on it |
| `ValueType` | what it was deserialized as, set even when the body was empty |
| `Matched` | whether any declared response matched |
| `Is<T>()`, `TryGet<T>(out T?)` | a typed test without a `switch` |
| `Require<T>()` | the body, or an exception naming what it actually was |
| `UnmatchedContent` | the raw body, kept **only** when nothing matched |

Nothing on this path throws: a method declaring `ApiResult` has said an error status is one of the
expected answers. `Require<T>()` is for the call site that has handled the alternatives and wants
the remaining failure to be loud.

Nothing is guessed either. An undeclared response leaves `Matched` false and keeps the body on
`UnmatchedContent` — the only clue as to which `[Response]` is missing.

### What it costs

This path **buffers** the response body, because choosing the type by inspecting it requires that.
The `Task<T>` path streams and does not pay for it. For an API that needs content-based
discrimination, buffering is not the expensive part of your day.

The decision is dynamic; the deserialization is not. The generator emits one branch per declared
response, each naming a concrete type, so this works under trimming and native AOT.

## Next

- [Retries](retries.md) — which failures are worth trying again
- [Serialization](serialization.md) — how bodies become objects
- [Diagnostics](diagnostics.md) — `RXH015`–`RXH018` cover the `[Response]` mistakes
