# Runax.Http.TestKit

Test support for [Runax.Http](https://github.com/runax-software/runax-http): assert on the request
your client *would* have sent, without a socket.

```csharp
using var harness = RunaxClientHarness.For<IRepositoryClient>();
harness.Transport.RespondsJson("""{"name":"runax-http","stars":1}""");

var repository = await harness.Client.GetAsync("runax-software", "runax-http");

repository.Name.ShouldBe("runax-http");
harness.Last.Path.ShouldBe("/repos/runax-software/runax-http");
harness.Last.Header("Accept").ShouldBe("application/json");
```

## Why a transport rather than a mock

The harness stands up a real container, the real generated client and the real runtime — over a
transport that records instead of sending.

That distinction is the whole point. Mocking the client interface tests nothing about your
attributes; a path template with a typo passes. Mocking the executor tests nothing about the
generated code. Almost everything a Runax.Http client does happens *before* a byte would reach the
network — template substitution, query encoding, header merging, serialization, transport selection
— so the thing worth asserting is the request that came out, and the only place it exists is at the
transport boundary.

## Reading the request

| Member | Gives you |
| --- | --- |
| `Last`, `Requests` | the most recent request, or all of them in order |
| `Method`, `Uri`, `Path`, `QueryString` | the basics |
| `Query(name)`, `QueryValues(name)` | query values, decoded |
| `Header(name)`, `HeaderValues(name)` | header values |
| `Body` | the body as text — the serialized JSON, usually |
| `Timeout`, `Content`, `Request` | the per-request timeout, raw content, raw request |

`QueryValues` exists because a collection argument expands to repeated keys, so reading a single
value would silently lose the rest.

## Scripting responses

```csharp
harness.Transport
    .RespondsJson("""{"name":"first"}""")       // queued, used once each in order
    .RespondsStatus(503)
    .Responds(TestResponse.Error(422, """{"errors":["nope"]}"""))
    .AlwaysResponds(TestResponse.Status(200));  // everything after the queue runs out
```

`AlwaysResponds` also takes a function of the request, for a stub that echoes something back.

`RunaxClientHarness.Responding<T>(response)` is the shorthand when every call gets the same answer.

### Simulating a transport failure

```csharp
harness.Transport
    .FailsOnce()                                 // a TransportException, as if never connected
    .RespondsJson("""{"name":"recovered"}""");
```

Needed for testing retries: a status code cannot express "the server was never reached", and that
is the case retrying actually exists for.

## Capabilities

`TestTransport` claims **every** capability by default, so the capability check never fails a test
that was about something else. Narrow it when the check *is* the point:

```csharp
using var harness = RunaxClientHarness.For<IRepositoryClient>(
    client => client.WithTimeout(TimeSpan.FromSeconds(5)),
    new TestTransport(capabilities: TransportCapabilities.None));

await Should.ThrowAsync<TransportCapabilityException>(() => harness.Client.GetAsync("o", "n"));
```

## Defaults worth knowing

- **A base address is supplied** (`https://runax.test`) when neither the interface nor the caller
  sets one, so a contract written without `[BaseAddress]` is still testable without every test
  providing one.
- **Reflection-based JSON** is the default serializer, deliberately: a test should not need a
  source-generated context to assert on a request body. This package is therefore *not* marked
  AOT-compatible — it is referenced by test assemblies, which are not trimmed or AOT-published, and
  claiming otherwise would be a promise nobody can use.
- **An unscripted request throws with a message saying so**, rather than returning an empty 200
  that makes the test fail somewhere unrelated.

## Your own container

For an integration test that already builds a host and just wants outbound HTTP recorded:

```csharp
var transport = services.AddRunaxHttp().AddTestTransport();
```

It returns the transport rather than the builder, since that is the handle the test needs.

## License

[MIT](https://github.com/runax-software/runax-http/blob/main/LICENSE)
