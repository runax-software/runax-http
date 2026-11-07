# Serialization

One serializer, chosen by you, for every transport.

## Where it sits

Above the transport boundary. Several HTTP libraries offer their own serializer and it is tempting
to use it — but that would mean the same client interface produced different JSON, and accepted
different JSON, depending on which transport happened to be selected. Transports never serialize;
by the time a request reaches one, the body is already encoded.

## Choosing one

`AddRunaxHttp()` registers **no** serializer. That is deliberate, and it is the one place this
library asks you for a line you might not expect:

```csharp
// Trim- and AOT-safe: a source-generated context, no reflection anywhere
services.AddRunaxHttp().UseSystemTextJson(new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});

// Convenient, and warns — reflection-based JSON does not survive trimming
services.AddRunaxHttp().UseSystemTextJson();
```

Reflection-based JSON cannot work under trimming or native AOT. Registering it silently as the
default would mean a library that advertises AOT support quietly opting every application out of
it, and the failure would surface as a `NotSupportedException` on the first request of a trimmed
deployment. So the convenient path is one explicit call that carries the warning, and the
AOT-correct path is the one without it.

A client with no serializer registered fails when it is first resolved, with a message naming both
options.

## System.Text.Json

`SystemTextJsonContentSerializer` serializes through `JsonTypeInfo` rather than the reflection-based
`JsonSerializer` overloads, so the type itself is trim- and AOT-safe. Whether *your* application is
depends on the options you give it.

`CreateReflectionBased()` is the convenient constructor, marked `[RequiresUnreferencedCode]` and
`[RequiresDynamicCode]` because it is. It defaults to `JsonSerializerDefaults.Web`: camelCase names,
case-insensitive reads — what an HTTP API almost always wants.

Options are frozen (`MakeReadOnly`) when the serializer is constructed, so a configuration mistake
surfaces at registration rather than on the first request.

## Per-client

```csharp
services.AddRunaxClient<ILegacyClient>()
        .UseSerializer(new SystemTextJsonContentSerializer(legacyOptions));
```

For the API that insists on snake_case while everything else in the application is camelCase.

## Writing your own

```csharp
public interface IContentSerializer
{
    string ContentType { get; }
    RequestContent Serialize<T>(T value);
    ValueTask<T?> DeserializeAsync<T>(Stream content, CancellationToken cancellationToken = default);
}
```

`ContentType` is sent as the request's `Content-Type` and offered in `Accept`. Implementations must
be thread-safe — one instance serves every call on a client.

Both methods are generic, and generated code calls them with the body parameter's **static** type.
That is what keeps serialization reflection-free: nothing has to inspect the object to find out
what it is.

An XML, MessagePack or Protobuf serializer slots in the same way.

## Empty bodies

A 204, and a 200 with no body, both deserialize to `default` rather than being handed to the
serializer. An empty stream given to a JSON reader fails with a parse error that says nothing about
the actual situation.

## Next

- [Native AOT and trimming](aot.md) — the whole picture, of which this is most of it
- [Responses and errors](responses.md) — what the deserialized value becomes
