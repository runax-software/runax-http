# From an OpenAPI document

A hand-written interface is still the point of Runax.Http. When an API already publishes an OpenAPI
document, though, typing that interface out is busywork — so `Runax.Http.OpenApi` writes the first
draft for you:

```bash
dotnet tool install -g Runax.Http.OpenApi
dotnet runax-http openapi https://api.example.com/openapi.json --namespace Shop --output Clients/
```

```text
  wrote Clients/IShopClient.cs
  wrote Clients/ShopModels.cs
  wrote Clients/ShopJsonContext.cs

Register it with:
  services.AddRunaxHttp().UseSystemTextJson(Shop.ShopJsonContext.Default.Options);
  services.AddRunaxClient<Shop.IShopClient>();
```

The document can be a file or an http(s) URL, in JSON or YAML, and Swagger 2.0, OpenAPI 3.0 or 3.1.
The format is read from the content, not the extension, since a URL usually has none.

## A starting point, not a build step

The output is ordinary source that you commit and then edit — rename a method, drop the endpoints
you never call, tighten a type. That is why it is a tool you run rather than something regenerated
on every build: regeneration would overwrite exactly the edits that make the interface yours.

So the tool refuses to overwrite a file that already exists. Pass `--force` to regenerate on
purpose, and review the diff like any other change.

The interface it writes is input to the Runax.Http generator, annotated as you would annotate it by
hand, so everything the generator checks applies: a mistake in the conversion is a build error with
an `RXH` id, not a runtime surprise.

## What you get

| File | Contents |
| --- | --- |
| `I{Name}Client.cs` | One method per operation, with `[BaseAddress]` from the first server |
| `{Name}Models.cs` | A `sealed record` per object schema, and a constants class per named string enum |
| `{Name}JsonContext.cs` | A `JsonSerializerContext` covering every type the interface sends or receives |

The name comes from the document's title — `Bookshop API` gives `IBookshopClient` — or from `--name`.
The namespace is the name unless you pass `--namespace`.

### Operations

- **Method names** come from `operationId`, Pascal-cased with `Async` added. An operation without
  one is named from its verb and path: `PUT /books/{book_id}/cover` is `PutBooksByBookIdCoverAsync`.
- **Path parameters** bind by name. One the C# name cannot match exactly — `book_id` becomes
  `bookId` — gets `[Path("book_id")]`.
- **Query parameters** get `[Query]`, with the key spelled out only where the generator's camelCase
  default would derive a different one. An object parameter is expanded to one key per property,
  which is what OpenAPI's default `form` style means; a `deepObject` parameter is typed as a
  dictionary with a `TODO` to build the keys yourself.
- **Header parameters** get `[Header("X-Name")]`. Cookie parameters are left out with a `TODO`,
  since there is no cookie binding.
- **Optional parameters** are nullable with `= null`, after the required ones; a
  `CancellationToken` is always last.
- **Bodies**: JSON is `[Body]` with its record; `application/x-www-form-urlencoded` is a
  `IReadOnlyDictionary<string, string>`; `multipart/form-data` is a sequence of `MultipartPart`;
  `text/*` is a raw `string` and anything else a raw `Stream`, with a `Content-Type` header when it
  is not the default. Where a body is offered in several media types, JSON is preferred.
- **Responses**: the first 2xx response's JSON schema is the return type, and an operation with no
  JSON success body returns `Task`. A success body that is not JSON — an image, a CSV — returns
  `Task<ApiResponse>`, since there is no type to deserialize it into.
- **`--api-result`** returns `Task<ApiResult>` instead, with a `[Response]` for every documented
  response that has a JSON body: exact codes, `4XX`-style ranges, and `default` as the fallback.
  That is the mode for an API whose errors carry a body worth reading.
- **`deprecated: true`** becomes `[Obsolete]`.

Summaries and descriptions become XML documentation, and every parameter is documented, so the
output builds cleanly in a project that generates documentation and treats warnings as errors.

### Schemas

| OpenAPI | C# |
| --- | --- |
| `object` with properties | a `sealed record`, `[JsonPropertyName]` on every property |
| `required` property | `required` member; anything else is nullable |
| `readOnly` property | never `required` — the server fills it in, so a request cannot have it |
| `nullable: true`, or `type: [x, 'null']` | a nullable type |
| `allOf` | one record with every part's properties merged |
| `oneOf` / `anyOf` | `JsonElement`, unless the only alternative is `null` |
| `array` | `IReadOnlyList<T>` |
| `additionalProperties` | `IReadOnlyDictionary<string, T>` |
| `string` with `date-time`, `date`, `time`, `uuid`, `uri`, `byte` | `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Guid`, `Uri`, `byte[]` |
| `integer` (`int64`) | `int` (`long`) |
| `number` (`float`, `decimal`) | `double` (`float`, `decimal`) |
| an object with no properties, or no type at all | `JsonElement` |
| a named string `enum` | `string`, plus a class of constants |

String enums are strings on purpose. A C# enum is sent by its member name — in JSON, and in a query
string by `RequestValueFormatter` — so `Status.Available` would put `Available` on the wire where the
API documents `available`. A string is exactly what the API takes on every path; the constants class
keeps the values discoverable:

```csharp
await client.ListBooksAsync(genre: Genre.NonFiction);   // ?genre=non-fiction
```

An inline object gets a record named after where it was found: the `series` property of `Book` is
`BookSeries`, and an inline request body for `updateBook` is `UpdateBookRequest`. A schema whose name
would shadow a type the code uses — `Task`, `Stream`, `Guid` — is numbered instead: `Task2`.

### The JSON context

It lists exactly the types the interface serializes, which is what the generator's `RXH024` check
looks for, so the output is ready for trimming and native AOT as generated. Register it through its
own options:

```csharp
services.AddRunaxHttp().UseSystemTextJson(ShopJsonContext.Default.Options);
```

It is generated with `DefaultIgnoreCondition = WhenWritingNull`, so an optional field you did not set
is left out of a request body rather than sent as `null` — which matters to an API that reads a PATCH
body field by field. Change it in the file if your API wants explicit nulls.

### Servers and authentication

The first server becomes `[BaseAddress]`, with its variables at their defaults; the others are
listed in the interface's remarks. A relative server URL is resolved against the document's URL when
it was fetched from one. Read from a file, there is nothing to resolve it against, so there is no
`[BaseAddress]` and the tool says to set one with `WithBaseAddress(...)`.

Security schemes are described in the interface's remarks with the call that configures each —
`UseBearerToken(...)` for a bearer token, or with a `headerName` for an API key in a header.

## Options

| Option | Meaning |
| --- | --- |
| `-o`, `--output <dir>` | Where to write the files. Default: the current directory. |
| `-n`, `--namespace <ns>` | The namespace. Default: the client's name. |
| `--name <name>` | The client's name: `--name Shop` gives `IShopClient`. Default: from the title. |
| `--api-result` | Return `Task<ApiResult>` with a `[Response]` per documented response. |
| `--force` | Overwrite files that already exist. |

The tool exits with `0` on success, `1` for a usage error, and `2` when the document cannot be read
or a file would be overwritten. Problems the OpenAPI reader reports, and anything the conversion had
to leave out, are printed as warnings; the generated code carries a `TODO` wherever it differs from
the document.

## Next

- [Defining a client](defining-a-client.md) — the attributes the output uses, for when you edit it
- [Responses and errors](responses.md) — `ApiResult` and `[Response]`, for `--api-result`
- [Native AOT & trimming](aot.md) — what the JSON context is for
