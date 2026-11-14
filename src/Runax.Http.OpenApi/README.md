# Runax.Http.OpenApi

A `dotnet` tool that turns an OpenAPI document into a [Runax.Http](https://www.nuget.org/packages/Runax.Http)
client: an annotated interface, the records it uses, and a `JsonSerializerContext` covering them.

```bash
dotnet tool install -g Runax.Http.OpenApi
dotnet runax-http openapi https://api.example.com/openapi.json --namespace Shop --output Clients/
```

The document can be a file or an http(s) URL, in JSON or YAML, and Swagger 2.0, OpenAPI 3.0 or 3.1.

The output is a starting point you commit and edit, not a build step, so the tool will not overwrite
a file that exists unless you pass `--force`. The interface is input to the Runax.Http generator,
annotated as you would write it by hand, so everything the generator checks still applies — and the
JSON context covers exactly what the interface serializes, so the result is ready for trimming and
native AOT as generated.

```csharp
services.AddRunaxHttp().UseSystemTextJson(Shop.ShopJsonContext.Default.Options);
services.AddRunaxClient<Shop.IShopClient>();
```

The tool's dependencies run on your machine and never reach your application: the generated code
references Runax.Http and the base class library, nothing else.

See [From an OpenAPI document](https://github.com/runax-software/runax-http/blob/main/docs/openapi.md)
for how each part of a document maps, and the options.
