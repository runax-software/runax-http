# Configuration

Two builders: one for what every client shares, one per client.

## Shared — `AddRunaxHttp()`

```csharp
services.AddRunaxHttp()
        .UseSystemTextJson(options)                       // or UseSystemTextJson() — see below
        .UseSerializer(myserializer)                      // any IContentSerializer
        .UseSerializer<MySerializer>()                    // resolved from the container
        .AddTransport(transport)                          // an instance
        .AddTransport<MyTransport>()                      // resolved from the container
        .AddTransport(provider => Build(provider))        // built from the container
        .UseDefaultTransport(HttpClientTransport.TransportName);
```

Safe to call repeatedly — `AddRunaxClient<T>()` calls it for you. Call it directly when you want to
change a shared default.

`UseSystemTextJson()` with no arguments carries a trimming warning, because reflection-based JSON
earns one. See [Serialization](serialization.md).

## Per client — `AddRunaxClient<T>()`

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .WithBaseAddress("https://api.example.com/v3")    // or a Uri
        .UseTransport(RestSharpTransport.TransportName)
        .WithTimeout(TimeSpan.FromSeconds(10))            // per attempt
        .WithTotalTimeout(TimeSpan.FromSeconds(30))       // the whole call, retries included
        .WithDefaultHeader("User-Agent", "my-service/1.0")
        .WithRetry(3)                                     // or WithRetry(options => …)
        .UseSerializer(legacySerializer)
        .Configure(options => options.SendAcceptHeader = false);
```

Each call overrides what the interface's attributes declared. The attributes are the default the
interface author suggests; registration is where the application decides.

## `RunaxClientOptions`

Everything `Configure(...)` can reach.

| Option | Default | What it does |
| --- | --- | --- |
| `BaseAddress` | from `[BaseAddress]` | the absolute address request paths resolve against |
| `TransportName` | from `[Transport]` | which transport this client's requests go over |
| `Timeout` | none | how long a single **attempt** may take when the method does not say |
| `TotalTimeout` | none | how long the whole **call** may take, retries and backoff included |
| `Serializer` | the shared one | this client's serializer |
| `DefaultHeaders` | from interface `[Header]`s | headers sent with every request |
| `Retry` | off | see [Retries](retries.md) |
| `SendAcceptHeader` | `true` | send `Accept` naming the serializer's content type |
| `MaxErrorContentLength` | 32 KB | how much of an error body to keep on the exception |

A request path **appends** to `BaseAddress`'s path rather than replacing it — see
[Defining a client](defining-a-client.md#url-composition).

`MaxErrorContentLength` exists because "unprocessable entity" occasionally comes back as a megabyte
of HTML, and that should not end up in a log line or an exception message. Set it to `0` to keep no
body at all.

## Ordering

Configuration applied after `AddRunaxClient<T>()` returns is still seen: a fluent chain reads as
one statement but runs as several, and the client may be resolved long after all of them. The same
holds for transports — one registered after a client that names it still counts, because the check
runs when the resolver is built rather than at each registration call.

## Reading from `IConfiguration`

There is no binder yet. Transport names are strings precisely so that
`"Transport": "restsharp"` in `appsettings.json` can resolve to something:

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .WithBaseAddress(configuration["Api:BaseAddress"]!)
        .UseTransport(configuration["Api:Transport"]!)
        .WithRetry(configuration.GetValue<int>("Api:Retries"));
```

A misspelled transport name from configuration fails when transports are first resolved, naming the
client and listing what exists.

## Without a container

The runtime does not require one — `HttpContractExecutor` takes its options directly, and the
generated client takes an executor:

```csharp
var options = new RunaxClientOptions { BaseAddress = new Uri("https://api.example.com") };
var transports = new HttpTransportResolver([new HttpClientTransport(new HttpClient())]);
var executor = new HttpContractExecutor(options, transports, serializer);
```

Constructing the generated class itself means naming it, which is generated detail rather than API,
so `AddRunaxClient<T>()` remains the supported route. This is here for a test or a one-off script.

## Next

- [Transports](transports.md), [Retries](retries.md), [Serialization](serialization.md)
