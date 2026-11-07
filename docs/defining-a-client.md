# Defining a client

A client is an interface. Each method is one request, described by attributes; the generator emits
the implementation at compile time.

```csharp
[BaseAddress("https://api.example.com/v3")]
[Header("User-Agent", "my-service/1.0")]
public interface IRepositoryClient
{
    [Get("/repos/{owner}/{name}")]
    Task<Repository> GetAsync(string owner, string name, CancellationToken ct = default);
}
```

## Request attributes

`[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]`, `[Head]`, `[Options]`. Each takes a path
template relative to the base address, with optional `{placeholder}` segments.

A method with no request attribute on a client interface is an error ([`RXH009`](diagnostics.md)) —
the generated class could not implement it, so the alternative is code that does not compile with a
message about a missing member rather than about the missing attribute.

## Binding parameters

| Attribute | Binds to |
| --- | --- |
| `[Path]` | a `{placeholder}` in the template |
| `[Query]` | a query string parameter |
| `[Body]` | the request body |
| `[Header("Name")]` | a request header |
| *(none)* | a `{placeholder}` of the same name |
| `CancellationToken` | nothing; cancels the request |

An unannotated parameter binds to a placeholder of the same name, because repeating the name in a
`[Path]` attribute is noise:

```csharp
[Get("/repos/{owner}/{name}")]
Task<Repository> GetAsync(string owner, string name, CancellationToken ct = default);
```

Anything else unannotated is an error ([`RXH005`](diagnostics.md)). Treating an unrecognized
parameter as a query parameter would be convenient right up to the first time someone adds an
`apiKey` parameter and finds it in their access logs.

Rename with the attribute when the wire name is not a legal or idiomatic C# name:

```csharp
[Query("per_page")] int perPage = 30
[Path("name")] string repository
```

### Collections and nulls

A `[Query]` parameter that is a collection expands to one repeated key per item:

```csharp
[Query] IEnumerable<string>? labels = null     // ?labels=bug&labels=docs
```

A null query value is **omitted** rather than sent empty. `labels=` means "match the empty label"
to some APIs and "no filter" to others; absence is the one thing that cannot be misread. The same
applies to a header: a null value removes it, which is how a method opts out of a client-wide
default header.

A null **path** value is an error at run time. `/repos//issues` addresses something other than what
the caller named, and would most likely 404 a long way from the cause.

### Query objects

A whole object works as a `[Query]` parameter, expanded to one key per property:

```csharp
public sealed record SearchFilter
{
    public string? State { get; init; }

    [Query("per_page")]
    public int PerPage { get; init; } = 30;

    public IEnumerable<string>? Labels { get; init; }
}

[Get("/search")]
Task<Results> SearchAsync([Query] SearchFilter filter, CancellationToken ct = default);
```

sends `?State=open&per_page=50&Labels=bug&Labels=docs`.

The generator resolves the properties at compile time, so this stays reflection-free and survives
trimming — which is why only a generator can offer it.

- **Field names are the property names, exactly as written.** Nothing in this library transforms a
  name, so `State` sends `State`. `[Query("per_page")]` on the property renames it, the same way it
  renames a parameter.
- **Inherited properties are included**, so a filter sharing a pagination base type works.
- **Null properties are omitted**, matching a null parameter. A null object contributes nothing at
  all.
- **Collection properties repeat their key**, matching a collection parameter.
- **Values go through the same formatter**, so a `DateTimeOffset` property spells exactly what a
  `DateTimeOffset` parameter would.
- **Nested objects are refused** ([`RXH023`](diagnostics.md#rxh023)). There is no single convention
  for naming a nested field — ASP.NET Core spells it `filter.state`, OpenAPI's `deepObject` spells
  it `filter[state]` — so flattening would have to guess. Flatten the type, or pass the inner
  object as its own parameter.

A **dictionary** keyed by string works too, contributing its own keys:

```csharp
[Get("/search")]
Task<Results> SearchAsync([Query] Dictionary<string, string?> filters, CancellationToken ct = default);
```

That is the one query shape whose keys are not known until run time, so it is the one resolved
there rather than by the generator.

A `[Query]` type the runtime cannot turn into text is [`RXH022`](diagnostics.md#rxh022), a build
error. Before that diagnostic existed, such a type fell through to `ToString()` and the server
received `?filter=SearchFilter { State = open, Page = 2 }` with no warning anywhere.

### Value formatting

Every value — path, query or header — is formatted the same way, invariantly:

| Type | Sent as |
| --- | --- |
| `string` | unchanged |
| `bool` | `true` / `false`, lowercase |
| `DateTime`, `DateTimeOffset` | ISO 8601 round-trip (`O`) |
| `DateOnly`, `TimeOnly` | ISO 8601 |
| `TimeSpan` | `c` (constant) format |
| `Guid` | `D` (hyphenated) |
| `enum` | the member name |
| anything `IFormattable` | invariant culture |

Invariant throughout, deliberately. There is no case where you want your machine's locale to decide
what a remote API receives, and a decimal point that becomes a comma on one developer's machine is
a bug that only reproduces there.

`true` rather than `True` because .NET's spelling is not the web's: every JSON and
OpenAPI-described API expects lowercase, and the ones that accept `True` do so by accident.

## Bodies

```csharp
[Post("/repos/{owner}/{name}/issues")]
Task<Issue> CreateIssueAsync(string owner, string name, [Body] NewIssue issue, CancellationToken ct = default);
```

The body is serialized by the configured [serializer](serialization.md), called with the
parameter's **static** type — which is what keeps serialization reflection-free.

`[Body(Format = BodyFormat.Raw)]` sends a `string`, `byte[]`, `ReadOnlyMemory<byte>` or `Stream`
as-is. A `Stream` is streamed rather than buffered, which needs a transport declaring
[`RequestStreaming`](capabilities.md).

`[Body(Format = BodyFormat.UrlEncoded)]` sends an `application/x-www-form-urlencoded` body from a
sequence of key/value pairs — a `Dictionary<string, string>`, or
`IEnumerable<KeyValuePair<string, object?>>` for values that still need formatting (which they get
from the same formatter as query values, so a date spells the same thing in both).

`[Body(Format = BodyFormat.Multipart)]` sends `multipart/form-data` from a sequence of
`MultipartPart`, and needs a transport declaring [`Multipart`](capabilities.md).

Neither maps an object's properties to fields or parts: that would mean reflecting over the type at
run time. A type the runtime cannot enumerate is [`RXH019`](diagnostics.md#rxh019), a build error,
rather than a trimming surprise.

At most one `[Body]` per method ([`RXH004`](diagnostics.md)).

A body on a `GET`, `HEAD` or `OPTIONS` is a warning ([`RXH010`](diagnostics.md)). It will be sent,
but caches, proxies and servers are entitled to ignore or reject it, so it is usually a `[Query]`
parameter that was marked `[Body]` by mistake.

## Headers

Three places, most specific winning:

```csharp
[Header("User-Agent", "my-service/1.0")]       // on the interface: every request
public interface IRepositoryClient
{
    [Put("/repos/{owner}/{name}/readme")]
    [Header("Content-Type", "text/markdown")]   // on the method: constant
    Task PutReadmeAsync(
        string owner,
        string name,
        [Header("Authorization")] string authorization,   // on a parameter: per call
        [Body(Format = BodyFormat.Raw)] string markdown,
        CancellationToken ct = default);
}
```

An `Accept` header naming the serializer's content type is added unless you set one, so a server
that content-negotiates knows what you can parse. Turn it off with
`SendAcceptHeader = false` (see [Configuration](configuration.md)).

## URL composition

A request path **appends** to the base address's path:

| Base address | Template | Request |
| --- | --- | --- |
| `https://host/api/v2` | `/users` | `https://host/api/v2/users` |
| `https://host/api/v2/` | `users` | `https://host/api/v2/users` |
| `https://host` | `/users` | `https://host/users` |

Note the first row. `Uri`'s own relative resolution would return `https://host/users`, discarding
the `/api/v2` someone deliberately configured. Appending is the rule least likely to surprise, and
it is applied once above the transport boundary so that it cannot vary by transport — see
[Architecture](architecture.md) for why that matters.

Placeholder values are escaped as a single path segment, so a value containing `/` or `?` stays one
segment instead of changing the shape of the URL. A template may carry its own query string
(`[Get("/search?scope=all")]`) and arguments are appended to it rather than replacing it.

An absolute template ignores the base address entirely, for the case where an API hands back a
fully qualified URL to follow.

## Limits

- **No generic interfaces** ([`RXH007`](diagnostics.md)) or generic methods
  ([`RXH008`](diagnostics.md)). The body and response types have to be known at compile time for
  serialization to work without reflection, so a type parameter cannot stand in for them.
- **Methods only** ([`RXH013`](diagnostics.md)). A client interface describes requests, and a
  request is a method call.
- **Interfaces only** ([`RXH001`](diagnostics.md)). Request attributes on a class do nothing, so
  the generator says so rather than ignoring them.

## Next

- [Responses and errors](responses.md) — return types, and what to do about an API with more than
  one response shape
- [Transports](transports.md) — choosing which HTTP library runs the request
- [Diagnostics](diagnostics.md) — every `RXH` code, what triggers it, and the fix
