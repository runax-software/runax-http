# Runtime proxy fallback

The generator is how Runax.Http is meant to be used. This page is about the case where it cannot
be: an interface the generator never saw.

```csharp
services.AddRunaxHttp().UseSystemTextJson();
services.AddRunaxClientWithProxyFallback<IRepositoryClient>();
```

That registers the generated implementation if there is one, exactly as `AddRunaxClient<T>()`
would. If there is not, it implements the interface at run time with a
[`DispatchProxy`](https://learn.microsoft.com/dotnet/api/system.reflection.dispatchproxy), reading
the same attributes the generator reads.

## When you need it

An interface has no generated implementation when the compilation declaring it did not run the
generator:

- it lives in a contracts assembly that references only `Runax.Http.Abstractions` — which carries
  the attributes but not the generator — and changing that assembly is not yours to do;
- it is loaded from a plugin, or produced by tooling at run time;
- it is declared in a language the generator does not run for.

If none of those apply, add a `Runax.Http` reference to the project declaring the interface and use
`AddRunaxClient<T>()`. `AddRunaxClient<T>()` never falls back on its own, and the error it throws
for a missing implementation names this method so the choice stays a deliberate one.

## What is the same

Everything after the request is built. The proxy hands the runtime the same `ClientRequest` a
generated method builds, and from there it is one code path: base address composition, header
merging, transport selection and its precedence, capability checks, retries and `[Retry]`, the
pipeline, telemetry, and response mapping including `[Response]` and `ApiResult`.

The request itself follows the generator's rules rather than rules of its own — the same binding
for an unannotated parameter, the same query shapes and [naming policies](defining-a-client.md),
the same body formats, the same response matching. Query keys go through the generator's own
formatter, compiled into the runtime from the same source file. The test suite runs every contract
it has through both paths and requires the requests to be identical, so the two cannot drift
without a failing build.

## What is different

**Mistakes surface at registration, not at build time.** The generator's errors become one
`InvalidOperationException` from `AddRunaxClientWithProxyFallback<T>()`, listing every problem
under the generator's id:

```text
IBrokenClient cannot be implemented by a runtime proxy:
  RXH003: the template '/repos/{owner}/{name}' contains '{name}', but 'IBrokenClient.GetAsync' has no parameter bound to it. ...
  RXH005: 'q' on 'IBrokenClient.SearchAsync' is not bound to any part of the request. ...
```

[Diagnostics](diagnostics.md) describes each id and its fix. It is later than a build error, but
still before the first request — a contract that cannot be implemented never reaches a call. The
generator's *warnings* (`RXH010`, `RXH018`, `RXH020`, `RXH024`) have no run-time equivalent and are
not reported; the behaviour they warn about is the same on both paths.

**It does not survive trimming or native AOT.** The interface, its parameters and any query object
are read by reflection, the implementation type is emitted when the client is first resolved, and
generic helpers are closed over the interface's types at run time. The method carries
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so calling it from a trimmed or AOT
project is a build warning at the call site rather than a failure in production. See
[Native AOT & trimming](aot.md).

**It costs more per call.** The attributes are read once, at registration, but each call still goes
through `DispatchProxy` and reads query-object properties by reflection. For most clients that is
noise next to the network; it is not nothing.

## Testing

`Runax.Http.TestKit`'s harness registers clients with `AddRunaxClient<T>()`, so it needs a generated
implementation. To test a proxied client, register it with the fallback over a recording transport
of your own — the same shape the harness uses, minus the lookup.
