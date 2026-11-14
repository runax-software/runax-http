# Native AOT and trimming

Both shipped libraries are marked `IsAotCompatible`, which runs the trim and AOT analysers over
them, and CI publishes a native-AOT binary that fails the build on any trim or AOT warning — in our
code or in anything we pull in — and then runs it.

## What is already safe

**Generated code uses no reflection.** A generated method builds a `ClientRequest` with string
literals and the parameters it was given. Serialization is called with the body parameter's static
type. Response dispatch emits one branch per declared `[Response]`, each naming a concrete type, so
the *decision* is dynamic and the *deserialization* is not.

**Client lookup uses no reflection.** Generated code registers a factory — a `new` expression —
from a `[ModuleInitializer]`, so `AddRunaxClient<T>()` finds it without scanning assemblies.

**The attributes are not read at run time.** Base address, transport name and constant headers are
seen by the generator at compile time and emitted into the registration. An attribute an
application never otherwise mentions is exactly the kind of thing trimming removes, so nothing
depends on it surviving.

**Value formatting uses no reflection.** Dates, enums, GUIDs and `IFormattable` go through
interface dispatch and pattern matching.

The runtime's only reflective call is `RuntimeHelpers.RunModuleConstructor`, used once per client
registration to force a module initializer that `typeof(T)` alone would not trigger.

The exception is opt-in and marked: `AddRunaxClientWithProxyFallback<T>()` implements an interface
the generator never saw by reflection and a `DispatchProxy`, and carries
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` so that calling it from a trimmed or AOT
project warns at the call site. Nothing else reaches that code. See
[Runtime proxy fallback](runtime-proxy.md).

## The one thing that can break it: JSON

Reflection-based `System.Text.Json` cannot work under trimming or native AOT. So the choice is
explicit rather than defaulted:

```csharp
// Clean: a source-generated context
services.AddRunaxHttp().UseSystemTextJson(new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});

// Warns, because it earns the warning
services.AddRunaxHttp().UseSystemTextJson();
```

`AddRunaxHttp()` registers **no** serializer. Silently registering the reflection-based one would
mean a library that advertises AOT support quietly opting every application out of it, and the
failure would arrive as a `NotSupportedException` on the first request of a trimmed deployment —
long after the decision was made and nowhere near it.

`SystemTextJsonContentSerializer` serializes through `JsonTypeInfo` rather than the reflection-based
overloads, so the type itself is clean; `CreateReflectionBased()` is marked
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` because that is what it is.

## A working AOT setup

```csharp
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Repository))]
[JsonSerializable(typeof(Issue))]
[JsonSerializable(typeof(NewIssue))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
```

```csharp
services.AddRunaxHttp().UseSystemTextJson(new JsonSerializerOptions
{
    TypeInfoResolver = ApiJsonContext.Default
});

services.AddRunaxClient<IRepositoryClient>();
```

Every type a client sends or receives needs a `[JsonSerializable]` entry — including the types
behind each `[Response]`, and the *collection* type for a method returning
`Task<IReadOnlyList<T>>`, since registering `T` does not cover `IReadOnlyList<T>`.

**You will not be the one tracking that.** The generator knows the complete set, so a type missing
from your context is [`RXH024`](diagnostics.md#rxh024) at build time — naming the type, the method,
the exact attribute line, and the context to add it to. Previously a forgotten entry surfaced as a
resolver error on the first call that needed it, in a deployed application, a long way from the
registration nobody updated.

It only fires when the compilation has a `JsonSerializerContext` at all, so staying on the
reflection-based serializer does not produce a stream of advice you did not ask for.

### Why the context is not generated for you

The obvious next step would be for the generator to *write* the context, since it knows the types.
It cannot, and the reason is worth recording so nobody tries again.

Output registered with Roslyn's `RegisterPostInitializationOutput` joins the compilation every
generator sees, so a `JsonSerializerContext` emitted that way **is** picked up and populated by
System.Text.Json's generator. Output registered with `RegisterSourceOutput` is not — the other
generator never sees it, and the emitted partial fails with `CS0534` for the abstract members
nobody implemented.

The type set can only come from analysing your contracts, which means `RegisterSourceOutput`. So
emission is not awkward, it is impossible; the diagnostic is the reachable 80%, and it closes the
part that actually hurt.

## How it is verified

`tests/Runax.Http.AotSmoke` references every shippable package and the generator, declares a client
interface, and runs it through a real container over a source-generated context — covering
`Task<T>`, `Task<ApiResponse<T>>`, `Task`, a `[Response]`-dispatched `ApiResult`, a serialized body,
a transport scope, and the capability check.

CI publishes it with `PublishAot` and `TreatWarningsAsErrors`, then executes the binary. The publish
catches anything the analysers can see; running it catches a warning-free build that still breaks at
run time, which is the failure that actually reaches users.

Add a project reference there whenever a new shippable package lands, or it goes unverified.

### A note on the ILCompiler advisory

The smoke test suppresses `GHSA-p3jp-9v83-76w9` by id. `PublishAot` resolves
`Microsoft.DotNet.ILCompiler` at the version implied by the installed SDK band, which trails the
serviced one, and pinning it with an explicit `PackageReference` makes the publish fail outright —
ILCompiler's own targets reject the reference. The real fix is a newer SDK. The suppression is
scoped to that one project and that one advisory, so anything else still fails the build.

## Trimming without AOT

The same applies: use a source-generated context. `PublishTrimmed` alone will warn about
`CreateReflectionBased()` and is right to.

## Next

- [Serialization](serialization.md) — the serializer contract and per-client overrides
- [Architecture](architecture.md) — why the generated code has nothing reflective in it
