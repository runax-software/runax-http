# Diagnostics

Every mistake the generator can see, as a build error with a location and a sentence saying what to
do about it.

This list is the main argument for a generator over a runtime proxy. A reflection-based client
discovers a mismatched placeholder or an unbindable parameter on the first call that reaches it, in
whatever environment gets there first.

The ids are tracked in
[`AnalyzerReleases.Shipped.md`](../src/Runax.Http.SourceGenerator/AnalyzerReleases.Shipped.md) and
[`AnalyzerReleases.Unshipped.md`](../src/Runax.Http.SourceGenerator/AnalyzerReleases.Unshipped.md),
because they end up in your build logs and `NoWarn` lists — retiring one or changing its severity is
a breaking change. A retired id is never reused for a different meaning, which is why `RXH014`'s
removal added `RXH019` rather than repurposing the number.

## Reference

| Id | Severity | Triggered by |
| --- | --- | --- |
| [RXH001](#rxh001) | Error | Request attributes on a class or struct |
| [RXH002](#rxh002) | Error | A return type that isn't `Task`-shaped |
| [RXH003](#rxh003) | Error | A `{placeholder}` with no parameter |
| [RXH004](#rxh004) | Error | More than one `[Body]` |
| [RXH005](#rxh005) | Error | A parameter bound to nothing |
| [RXH006](#rxh006) | Error | More than one `CancellationToken` |
| [RXH007](#rxh007) | Error | A generic client interface |
| [RXH008](#rxh008) | Error | A generic client method |
| [RXH009](#rxh009) | Error | A member with no request attribute |
| [RXH010](#rxh010) | **Warning** | A body on `GET`, `HEAD` or `OPTIONS` |
| [RXH011](#rxh011) | Error | A `[BaseAddress]` that isn't absolute |
| [RXH012](#rxh012) | Error | Two parameters bound to one placeholder |
| [RXH013](#rxh013) | Error | A property or event on a client interface |
| [RXH015](#rxh015) | Error | `[Response]` without `Task<ApiResult>` |
| [RXH016](#rxh016) | Error | `Task<ApiResult>` without `[Response]` |
| [RXH017](#rxh017) | Error | Two `[Response]`s with the same condition |
| [RXH018](#rxh018) | **Warning** | A `[Response]` that can never match |
| [RXH019](#rxh019) | Error | A body parameter typed wrong for its format |

---

### RXH001

**Request attributes are only valid on interfaces.**

```csharp
public class RepositoryClient            // ✗
{
    [Get("/repos/{owner}")] public Task<Repository> GetAsync(string owner) => null!;
}
```

Nothing is generated for a class, so the attributes would do nothing. Reported rather than ignored
because the author has written what looks like a client and would otherwise find out when the
methods they expected to be implemented don't exist.

**Fix:** move the methods to an interface and inject that.

### RXH002

**Unsupported client method return type.**

```csharp
[Get("/repos/{owner}")] Repository Get(string owner);        // ✗ synchronous
```

**Fix:** use `Task`, `Task<T>`, `Task<ApiResponse>`, `Task<ApiResponse<T>>` or `Task<ApiResult>`.

HTTP is asynchronous, so a synchronous signature could only be implemented by blocking, which
deadlocks often enough that it is not offered. See [Responses](responses.md).

### RXH003

**Path template placeholder has no parameter.**

```csharp
[Get("/repos/{owner}/{name}")]
Task<Repository> GetAsync(string owner);                      // ✗ {name} unfilled
```

**Fix:** add a parameter named `name`, or mark one `[Path("name")]`.

### RXH004

**A request can have only one body.**

```csharp
[Post("/issues")] Task CreateAsync([Body] NewIssue a, [Body] Labels b);   // ✗
```

**Fix:** combine them into one type, or send the rest as `[Query]` or `[Header]`.

### RXH005

**Parameter is not bound to anything.**

```csharp
[Get("/issues")] Task<Issue[]> ListAsync(int page);           // ✗ no {page} in the template
```

**Fix:** mark it `[Query]`, `[Path]`, `[Body]` or `[Header]`, or name it after a placeholder.

Treating an unrecognized parameter as a query parameter would be convenient right up to the first
time someone adds an `apiKey` parameter and finds it in their access logs.

### RXH006

**A method can take only one CancellationToken.**

**Fix:** keep one, as the last parameter.

### RXH007

**Client interfaces cannot be generic.**

```csharp
public interface ICrudClient<T> { }                           // ✗
```

A client is generated once per interface, and a generic interface has no single implementation to
generate.

**Fix:** declare a non-generic interface per concrete API.

### RXH008

**Client methods cannot be generic.**

The body and response types have to be known at compile time for serialization to work without
reflection, so a type parameter cannot stand in for them.

**Fix:** declare one method per concrete type. For a response whose shape varies at run time, use
[`[Response]` and `ApiResult`](responses.md#more-than-one-response-shape).

### RXH009

**Client interface member has no request attribute.**

```csharp
public interface IRepositoryClient
{
    [Get("/repos")] Task<Repository[]> ListAsync();
    Task<int> CountAsync();                                   // ✗
}
```

The generated client could not implement it, so the build would fail with a message about a missing
member rather than about the missing attribute.

**Fix:** mark it, or move it to a different interface.

### RXH010

**This method's HTTP method does not normally carry a body.** *(warning)*

```csharp
[Get("/search")] Task<Result[]> SearchAsync([Body] Query query);   // ⚠
```

It will be sent, but caches, proxies and servers are entitled to ignore or reject it — so this is
usually a `[Query]` parameter marked `[Body]` by mistake. A warning rather than an error because
some APIs genuinely do this.

### RXH011

**`[BaseAddress]` must be an absolute URI.**

```csharp
[BaseAddress("api.example.com")]                              // ✗ no scheme
```

**Fix:** `[BaseAddress("https://api.example.com")]`.

### RXH012

**Two parameters are bound to the same placeholder.**

```csharp
[Get("/repos/{id}")] Task<Repository> GetAsync([Path("id")] int a, [Path("id")] int b);   // ✗
```

Only one value can fill a placeholder, so which one wins would be arbitrary.

### RXH013

**Client interfaces can only declare methods.**

```csharp
public interface IRepositoryClient
{
    string BaseUrl { get; }                                   // ✗
}
```

A client interface describes requests, and a request is a method call.

**Fix:** configure it at registration instead — see [Configuration](configuration.md).

### RXH015

**`[Response]` requires a `Task<ApiResult>` return type.**

```csharp
[Get("/widgets/{id}")]
[Response(200, typeof(Widget))]
Task<Widget> GetAsync(int id);                                // ✗
```

A method describing more than one possible response shape has to return `Task<ApiResult>`, because
that is the only return type that can hold whichever one arrived.

### RXH016

**`Task<ApiResult>` requires at least one `[Response]`.**

Without one, every call would come back unmatched.

**Fix:** add a `[Response(...)]` per shape the API can answer with, or return `Task<T>` if there is
only one.

### RXH017

**Two responses match the same condition.**

```csharp
[Response(200, typeof(Widget))]
[Response(200, typeof(Gadget))]                               // ✗ unreachable
```

The first wins and the rest can never run, so which type a response deserializes to would depend on
declaration order rather than on anything visible at the call site.

### RXH018

**This response can never match.** *(warning)*

```csharp
[Response(typeof(Widget))]        // unconditional — matches everything
[Response(404, typeof(NotFound))] // ⚠ never reached
```

**Fix:** put the unconditional `[Response]` last. It is the fallback.

### RXH019

**This parameter type cannot be sent in that body format.**

```csharp
[Post("/form")]
Task SubmitAsync([Body(Format = BodyFormat.UrlEncoded)] Widget widget);   // ✗
```

Form and multipart bodies are built from the argument's own shape rather than serialized, so the
type has to be one the runtime can enumerate:

| Format | Needs |
| --- | --- |
| `UrlEncoded` | a sequence of `KeyValuePair<string, …>` — a `Dictionary<string, string>`, or `IEnumerable<KeyValuePair<string, object?>>` for values that still need formatting |
| `Multipart` | a sequence of `MultipartPart`, built at the call site |
| `Raw` | `string`, `byte[]`, `ReadOnlyMemory<byte>` or `Stream` |

Mapping an object's properties to form fields or parts would mean reflecting over the type at run
time, which is the one thing this library does not do — so it is a build error rather than a
trimming surprise.

**Fix:** use `BodyFormat.Json`, or project the object into a dictionary at the call site where the
names are visible.

> `RXH014` previously refused `UrlEncoded` and `Multipart` outright. Both are emitted now, so it is
> retired — recorded under *Removed Rules* in
> [`AnalyzerReleases.Unshipped.md`](../src/Runax.Http.SourceGenerator/AnalyzerReleases.Unshipped.md)
> rather than quietly dropped, and its number is not reused for anything else.

## Suppressing

The normal mechanisms apply — `#pragma warning disable RXH010`, a `<NoWarn>` entry, or an
`.editorconfig` severity override:

```ini
[*.cs]
dotnet_diagnostic.RXH010.severity = none
```

Worth doing for `RXH010` if your API legitimately takes a body on `GET`. The errors are errors
because the alternative is code that does not compile anyway, or code that compiles and sends the
wrong request.
