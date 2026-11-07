# Changelog

All notable changes to Runax.Http are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
All packages in the repository are versioned together.

## [Unreleased]

### Added

- **`WithTotalTimeout`** — a budget for the whole call, retries and the waits between them
  included. `Timeout` bounds a single *attempt*, which reads like a call budget and is not one: the
  transport request carrying it is built once, before the retry loop, so every attempt gets the
  same allowance and the caller waits for all of them plus the backoff. Five attempts at thirty
  seconds is over two and a half minutes with nothing in the configuration looking wrong, and
  nothing capped it.
  - Exceeding the budget throws `TransportException` rather than surfacing as a cancellation,
    matching how a per-attempt timeout is already reported. A caller's own cancellation still
    surfaces as a cancellation.
  - Implemented as a linked token rather than a timeout handed to the transport, because the budget
    has to cover the waits *between* attempts, which happen in the executor rather than inside a
    send. Translated outside the retry loop on purpose: thrown inside, a `TransportException` would
    be caught by the loop's own retry guard and the budget would buy more attempts instead of
    ending the call.
  - Unbounded by default. Adding a budget nobody asked for would change the behaviour of every
    existing client on upgrade.
  - `Timeout`'s documentation now says "per attempt", which it never did.
- **Query objects.** A whole object as a `[Query]` parameter is expanded to one key per property,
  resolved by the generator at compile time — so it needs no reflection and survives trimming,
  which is why only a generator can offer it. Field names are the property names exactly as
  written, because nothing in this library silently transforms a name; `[Query("per_page")]` on a
  property renames it. Inherited properties are included, null properties are omitted, collection
  properties repeat their key, and values go through the same formatter as every other query value
  so a date spells the same thing either way.
  - **Nested objects are refused** rather than flattened. ASP.NET Core spells a nested key
    `filter.state` and OpenAPI's `deepObject` spells it `filter[state]`; picking one silently would
    be the same guess the new diagnostics exist to prevent.
- **Dictionary query parameters.** A sequence of `KeyValuePair<string, _>` contributes its own keys
  — the one query shape whose keys are not known until run time, and so the one resolved there.
- Two new diagnostics. `RXH022` rejects a `[Query]` type the runtime cannot turn into text;
  `RXH023` names the property of an otherwise-expandable object that cannot be sent.
- **`RXH024` catches a type missing from your `JsonSerializerContext`**, closing the last sharp edge
  in the AOT story. The generator already knows every type your contracts send and receive, so you
  no longer have to keep the context in step by hand: a missing registration is a build warning
  naming the type, the method, the exact `[JsonSerializable]` line and the context to add it to.
  Previously it surfaced as a resolver error on the first call that needed it, in a deployed
  trimmed application, a long way from the registration nobody updated.
  - Covers `[Body]` parameters that reach the serializer, `Task<T>`, `Task<ApiResponse<T>>` and
    every `[Response]` type — including the *collection* type for `Task<IReadOnlyList<T>>`, since
    registering `T` does not cover `IReadOnlyList<T>`. That is the easiest gap to miss by hand.
  - Stays quiet when the compilation has no context at all, because then you are on the
    reflection-based serializer, which is a supported choice that already carries its own warning.
  - Two deliberate blind spots, both to avoid warning when nothing is wrong: bare primitives and
    `string` are never reported, since they resolve for free whenever any registered type has a
    property of that type; and a type registered by *any* context counts, since nothing at compile
    time can tell which context is wired to which client.
  - A warning rather than an error, because a mixed setup — one client on a source-generated
    context, another deliberately on the reflection-based serializer — is legitimate and would
    otherwise fail a build with no recourse but suppression. Under warnings-as-errors it fails the
    build anyway.
  - **The context itself cannot be generated**, and the reason is recorded in `docs/aot.md` so
    nobody tries again. Roslyn output registered with `RegisterPostInitializationOutput` joins the
    compilation every generator sees, so a `JsonSerializerContext` emitted that way *is* populated
    by System.Text.Json's generator — verified. Output registered with `RegisterSourceOutput` is
    not: the other generator never sees it and the partial fails with `CS0534`. The type set can
    only come from analysing the contracts, which means `RegisterSourceOutput`, so emission is
    impossible rather than merely awkward.

- **`[Retry]` on a method**, for the one flaky endpoint on an otherwise healthy API. Surfaces an
  attempt count, `DelayMilliseconds` (milliseconds because a `TimeSpan` cannot be an attribute
  argument) and `AllowNonIdempotent`. It refines the client's registered settings rather than
  replacing them: backoff curve, jitter, status-code set and `Retry-After` handling still come from
  the registration, and only what the attribute names changes.
  - **A method's declaration wins over the client's attempt count**, which is the opposite of how
    `[Transport]` resolves. Deliberately: a transport is a *competing* claim about one fact — which
    stack sends the request — so one side has to lose and the operator knows the deployment. A
    retry count is a *refinement*, so there is no tie to break and the more specific statement
    applies. A per-request timeout and a method's `[Header]` over a client-wide default header
    already work this way.
  - **`WithoutRetries()`** on a client turns retrying off and ignores every declaration. The
    operator keeps the last word, because a client that cannot be stopped from retrying is a
    liability during an incident. Distinct from `WithRetry(1)`, which only says the client has no
    policy of its own and leaves a declaration free to supply one.
  - A declaration turns retrying on by itself, without a client-wide `WithRetry` first — otherwise
    the attribute would do nothing in the common case, since retrying is off by default.
    `[Retry(1)]` is meaningful in the other direction: it exempts one method from a client-wide
    policy.
  - The safety defaults are not bypassable. A streamed body is still never retried, since the first
    attempt consumed it. A non-idempotent method still needs `AllowNonIdempotent`, which lives on
    the attribute because the interface author is the one who knows whether a given endpoint has an
    idempotency key — the client-wide `RetryNonIdempotentMethods` opts in every method at once and
    is the blunter instrument.
  - Two new diagnostics. `RXH020` (warning) catches a `[Retry]` on a `POST` or `PATCH` without
    `AllowNonIdempotent`, where the attribute would silently do nothing. `RXH021` catches an
    attempt count below one.
- **The request pipeline** — logging, authentication and correlation as middleware that works on
  every transport. `DelegatingHandler` is where these would normally live and only exists in
  `HttpClient`-land, so anything built on it could not be reused for RestSharp; this sits above
  `IHttpTransport` instead, for the same reason serialization, retries and the capability check do.
  Each stage is independently opt-in and a client with no stages pays nothing — the pipeline is the
  transport call.
  - **It runs inside the retry loop, once per attempt, with a `RequestContext` created once per
    call.** Neither position serves everything on its own: outside, a refreshed token never reaches
    a retry; inside with nowhere to keep per-call state, a correlation id changes on every try. So
    stages re-run per attempt and anything that must stay constant lives on the context —
    `CorrelationId` is assigned with `??=` and `Attempt` tells the tries apart.
  - **Ordering is explicit**, from `IHttpMiddleware.Order` rather than registration order, so
    reordering a fluent chain cannot silently change behaviour. Correlation outermost, then
    logging, then authentication closest to the wire; equal values keep registration order.
  - **Logging** writes a line per attempt with method, address, status, elapsed, transport and
    attempt number, through an `ILogger` categorised by the contract so one client can be turned up
    alone. It **never logs bodies** — not configurable, because a body is the likeliest place for a
    credential and a switch like that gets enabled in an incident and left on. Header values are an
    allow-list, empty by default; `Authorization`, `Proxy-Authorization`, `Cookie` and `Set-Cookie`
    render as `***` even when named, which answers "was auth attached?" without carrying the token.
  - **Authentication** asks its provider on every attempt, so a token refreshed between a failure
    and its retry is the one that gets used, and replaces the header rather than appending so a
    retry does not carry both. A null token sends the request unauthenticated and clears any header
    a previous attempt set.
  - **Correlation** propagates a stable id, taking the trace id and W3C `traceparent` from an
    ambient `Activity` when there is one so the call joins the trace already running. A header the
    caller set explicitly is left alone.
  - A stage may only change headers. `RequiredCapabilities` is derived from the body, proxy,
    timeout and version and the capability check runs before the pipeline, so a stage altering one
    of those could introduce a requirement nothing had checked.

### Changed

- **Query keys are camelCased from their identifiers by default**, and a `[QueryNaming]` policy
  covers the API that follows another convention — `SnakeCaseLower` for a Python or Ruby backend,
  plus the upper and kebab variants and `AsDeclared` for verbatim.
  - camelCase because the body serializer already does it to the same type: a filter sent as
    `[Body]` serializes to `{"perPage":…}`, so sending `?PerPage=` for the identical type would
    have one library spelling one type two ways — and since most APIs want the camelCase one, the
    alternative cost an attribute on nearly every property of every filter object.
  - Applies to parameter names as well as object properties. A policy that transformed fields but
    left parameters alone would leave half a query string in the other convention, which is worse
    than either consistent choice. In practice the default changes nothing for parameters, since
    C# parameter names are already camelCase; a non-camelCase one now camelCases, and
    `[Query(Naming = QueryNaming.AsDeclared)]` keeps the 1.0 behaviour.
  - An acronym run stays one word and a digit run is its own, so `HTTPReferer` is `http_referer`
    rather than `h_t_t_p_referer`, and `Top10Items` is `top_10_items`.
  - Applied by the generator, so every key is a literal in the emitted code and nothing is
    transformed per request. Dictionary keys are left alone — those are data, not identifiers.
  - Precedence is explicit `[Query("name")]`, then `[Query(Naming = …)]` on the parameter, then
    `[QueryNaming]` on the interface, then camelCase.

- **`RXH014` is retired and `RXH019` replaces it.** `RXH014` refused the two formats outright;
  there is nothing left for it to refuse. `RXH019` covers the real mistake instead: a body
  parameter whose type cannot be sent in the format it declared, including `Raw` with something
  that is not a `string`, `byte[]`, `ReadOnlyMemory<byte>` or `Stream`.
- The retirement is recorded under *Removed Rules* in `AnalyzerReleases.Unshipped.md`, with the
  1.0.0 rule set moved to `AnalyzerReleases.Shipped.md`. The number is not reused for a different
  meaning — these ids reach consumers' build logs and `NoWarn` lists, so a silent change of meaning
  would be worse than a retirement.

### Fixed

- **A `[Query]` object no longer sends `ToString()` to your server.** `[Query] SearchFilter filter`
  compiled clean, raised no diagnostic, and sent
  `?filter=SearchFilter%20%7B%20State%20%3D%20open...` — the record's `ToString()`, URL-encoded. A
  non-record class sent its type name. `RequestValueFormatter`'s `ToString()` fallback is right for
  an unknown `IFormattable` but should never have been reachable by a type the generator could see
  was wrong, and a multi-field filter object is the most natural thing to try.
- **A rebuilt request no longer changes underneath a transport.** `TransportRequest` is documented
  as immutable and a transport is entitled to hold on to one, but the pipeline handed over its live
  header list — so on a retry the next attempt's header changes rewrote a request that had already
  been sent. It showed up as the first attempt appearing to carry the second attempt's token, and
  was caught by the test written to prove token refresh worked.
- **`BodyFormat.UrlEncoded` and `BodyFormat.Multipart`** are now emitted, completing the body
  formats the attribute has always advertised. A form body comes from a sequence of key/value pairs
  — a `Dictionary<string, string>`, or `IEnumerable<KeyValuePair<string, object?>>` for values that
  still need formatting, which they get from the same formatter as query values so a date spells
  the same thing in both. A multipart body comes from a sequence of `MultipartPart`, and stays
  structured all the way to the transport, because boundary generation is genuinely native to each
  HTTP library.
- Neither maps an object's properties to fields or parts. That would mean reflecting over the type
  at run time, which is the one thing this library does not do — so a type the runtime cannot
  enumerate is a build error rather than a trimming surprise.

## [1.0.0] - 2026-11-07

### Added

- **`WithTotalTimeout`** — a budget for the whole call, retries and the waits between them
  included. `Timeout` bounds a single *attempt*, which reads like a call budget and is not one: the
  transport request carrying it is built once, before the retry loop, so every attempt gets the
  same allowance and the caller waits for all of them plus the backoff. Five attempts at thirty
  seconds is over two and a half minutes with nothing in the configuration looking wrong, and
  nothing capped it.
  - Exceeding the budget throws `TransportException` rather than surfacing as a cancellation,
    matching how a per-attempt timeout is already reported. A caller's own cancellation still
    surfaces as a cancellation.
  - Implemented as a linked token rather than a timeout handed to the transport, because the budget
    has to cover the waits *between* attempts, which happen in the executor rather than inside a
    send. Translated outside the retry loop on purpose: thrown inside, a `TransportException` would
    be caught by the loop's own retry guard and the budget would buy more attempts instead of
    ending the call.
  - Unbounded by default. Adding a budget nobody asked for would change the behaviour of every
    existing client on upgrade.
  - `Timeout`'s documentation now says "per attempt", which it never did.

- **`Runax.Http.TestKit`** — `RunaxClientHarness.For<TContract>()` stands up a real container, the
  real generated client and the real runtime over a `TestTransport` that records instead of sending.
  - A transport rather than a mock, because that is where the interesting state is. Mocking the
    client interface tests nothing about your attributes — a path template with a typo passes — and
    mocking the executor tests nothing about the generated code. Almost everything a client does
    happens before a byte would reach the network, so the request that came out is the thing worth
    asserting, and the transport boundary is the only place it exists.
  - `RecordedRequest` reads back the method, URI, path, decoded query values (including repeated
    keys, which a single-value accessor would silently lose), headers and the body as text.
  - Responses are queued with `Responds`/`RespondsJson`/`RespondsStatus`, or answered in bulk with
    `AlwaysResponds`; `FailsOnce()` simulates a request that never reached a server, which is what
    retry tests need and no status code can express. An unscripted request throws saying so rather
    than returning an empty 200 that fails the test somewhere unrelated.
  - Claims every capability by default so the capability check never fails a test that was about
    something else, and can be narrowed when the check is the point.
  - Supplies a base address when neither the interface nor the caller does, and defaults to
    reflection-based JSON on purpose. The package is therefore **not** marked AOT-compatible: it is
    referenced by test assemblies, which are not trimmed, and claiming otherwise would be a promise
    nobody can use.

- **`Runax.Http.Transports.RestSharp`** — the second transport, and the thing that turns "swappable"
  from a claim into a tested property. The same client interface runs over it or over `HttpClient`
  with nothing above `IHttpTransport` changing.
  - It declares a genuinely **different** capability set: no `RequestStreaming` and no
    `ResponseStreaming`, where the `HttpClient` transport has both. RestSharp's `ExecuteAsync` reads
    the whole response before returning and its body parameters hold a value rather than a stream,
    so a client streaming a large upload is told so rather than having it quietly buffered. That
    divergence is the capability model earning its keep rather than a shortcoming.
  - `AutomaticDecompression`, `Cookies` and `AutomaticRedirect` are derived from the options
    actually in force, so the declared capabilities describe the instance rather than the type.
  - Registerable more than once under different names, which is how a proxy pool works and the
    reason a transport is identified by a name rather than a type.
- **Transport conformance suite** — one shared, inherited suite run against a real Kestrel server on
  a loopback port; a transport test project is now a single class returning a transport. It covers
  methods, the raw request line, headers including content and repeated ones, bodies, every status
  code, binary integrity, response lifetime, connection failures, cancellation, timeouts, and
  whether the declared capabilities match reality — a capability a transport does *not* declare is
  asserted absent rather than skipped.
  - It found two real bugs in the RestSharp transport on its first run.
    `SetErrorExceptionOnUnsuccessfulStatusCode` is RestSharp's default, so every 404 arrived as
    `ResponseStatus.Error` and surfaced as a `TransportException` — an ordinary error status turned
    into a transport failure, which is exactly the leak the boundary forbids. And the URI test was
    asserting the server's decoded path, which hid whether `%2F` survived the wire; it now asserts
    the raw request line, which is what "sent exactly as given" actually means.

- **Generator tests** — a harness that drives the generator over source snippets and asserts on
  what it reported and emitted, plus one test per diagnostic (40 tests across three target
  frameworks). Eighteen diagnostics had no automated coverage at all, which is how `RXH001` sat
  declared and unreachable; the ids also reach consumers' build logs and `NoWarn` lists, so they
  are API and now have tests to say so in review. The harness tests itself too, since a reference
  list that silently came up empty would make every "produces no diagnostics" assertion pass for
  the wrong reason.
- **Documentation** — eleven pages under `docs/`: defining a client, responses and errors,
  transports, capabilities, retries, serialization, configuration, the full `RXH001`–`RXH018`
  diagnostic reference, architecture, writing a custom transport, and native AOT. Every diagnostic
  in that reference was verified to actually fire against a scratch project rather than documented
  from the source. Pages for testing, observability and transport conformance are deliberately
  absent until the features exist.

- **`Runax.Http`** — the package to install: the source generator, the request runtime, the
  dependency-injection wiring, and the `HttpClient` transport.
  - **Source generator.** An annotated interface in, an implementation out, at compile time, with
    no reflection in the generated code. Supports `Task`, `Task<T>`, `Task<ApiResponse>` and
    `Task<ApiResponse<T>>`; `[Query]`, `[Path]`, `[Body]`, `[Header]` and `CancellationToken`
    parameters; `[BaseAddress]`, `[Transport]` and constant `[Header]`s on the interface or method.
    A parameter with no attribute binds to a path placeholder of the same name; anything else is an
    error rather than a guess.
  - **Fourteen diagnostics, `RXH001`–`RXH014`**, each a build error with a location and a sentence
    saying what to do: an unmatched `{placeholder}`, two `[Body]` parameters, an unbindable
    parameter, a synchronous return type, a generic interface or method, a non-absolute
    `[BaseAddress]`, two parameters bound to one placeholder. Tracked in
    `AnalyzerReleases.Unshipped.md`, because these ids reach consumers' build logs.
  - **`HttpClientTransport`**, declaring what a stock handler can actually do — notably *not*
    per-request proxies, which belong to the handler and are shared by every request through the
    client, and *not* automatic decompression, which is off by default.
  - **Transport selection** with a documented precedence: `TransportScope.Use(...)` beats
    `UseTransport(...)` at registration, which beats `[Transport]` in the source, which beats the
    only registered transport. Asking for a name nothing is registered under lists the ones that
    are, since that is almost always a typo or a missing transport package.
  - **Transport names are strings, exposed per transport as a constant** —
    `HttpClientTransport.TransportName` — rather than an enum. An enum would have to live in
    `Runax.Http.Abstractions`, so no transport package could name itself without a release of the
    one package every transport already references. A type parameter would not work either: a
    transport's identity is its registration, and several of one type can be registered under
    different names with different handlers or proxies.
  - **Configured transport names are validated when transports first resolve**, not on the request
    that asks for them. A misspelled `UseTransport(...)` or `[Transport(...)]` — including on a
    method, where a typo could otherwise lie dormant until that one method ran — fails naming the
    client and listing what is registered. Overriding a declared name at registration suppresses
    the check, which is the escape hatch for a consumer who does not want the transport an
    interface asks for.
  - **`AddRunaxHttp()` and `AddRunaxClient<T>()`**, with per-client base address, transport,
    timeout, default headers and serializer. Client lookup goes through a registry that generated
    code populates from a module initializer, so registration needs no assembly scanning and works
    under trimming and native AOT.
  - **URI composition in one place.** A request path appends to the base address's path rather than
    replacing it, path values are escaped as a single segment, query values are formatted
    invariantly, a collection expands to repeated keys, and a null is omitted rather than sent
    empty. Transports never build URLs — the difference between a transport swap being invisible
    and it quietly changing which URL gets hit.
  - **`SystemTextJsonContentSerializer`**, serializing through `JsonTypeInfo` so a source-generated
    context needs no reflection. `AddRunaxHttp()` registers no serializer by default: the
    convenient reflection-based one is one explicit call that carries a trimming warning, rather
    than a silent default that opts every application out of AOT.
- **`[Response]` and `ApiResult`** — a response type chosen at run time, for the two cases a single
  return type cannot express.
  - **A different shape per status code**: `[Response(200, typeof(Widget))]`,
    `[Response(404, typeof(NotFound))]`, or a whole class at once with
    `MinStatusCode`/`MaxStatusCode`.
  - **An API that answers 200 whatever happened** and puts the outcome in the body. A status code
    cannot tell those apart, so discriminate on a top-level JSON property — presence
    (`WhenJsonProperty`) or value (`WhenJsonPropertyEquals`, which compares against the JSON
    spelling so `"false"` matches `"success": false`) — or on a header. Without this a client
    deserializes an error envelope into the success type and hands back an object with default
    fields, which looks like success until the fields get used.
  - The first declaration whose conditions hold wins, in source order, and conditions on one
    attribute are ANDed. Order rather than a specificity rule, so which response wins is visible
    in the source instead of depending on which overlapping condition the library considers
    narrower. `RXH018` warns about a declaration placed after an unconditional one.
  - Nothing is guessed: an undeclared response leaves `Matched` false and the raw body on
    `UnmatchedContent`. Nothing throws either — a method declaring `ApiResult` has said an error
    status is an expected answer — with `Require<T>()` for the call site that wants it loud.
  - Each branch names a concrete type, so the decision is dynamic but the deserialization is not,
    and the whole path is exercised by the native-AOT smoke test. It does buffer the body, which
    the streaming `Task<T>` path avoids; that is the price of deciding from the content.
  - Four new diagnostics, `RXH015`–`RXH018`.
- **Retries**, off by default and configurable per client with `WithRetry(3)` or
  `WithRetry(options => ...)`: attempt count, base and maximum delay, constant/linear/exponential
  backoff, jitter, `Retry-After` handling, and which status codes count. They sit above the
  transport boundary, so every transport gets the same behaviour and swapping one does not change
  retry semantics.
  - Three defaults are safety decisions rather than taste. Only idempotent methods are retried —
    a retried `POST` that timed out after the server committed it creates the thing twice. A
    request whose body is a stream is never retried, because the stream is already consumed and
    buffering it would mean holding an upload of unknown size in memory. And 500 is not in the
    retryable set: 408, 429, 502, 503 and 504 are.
  - `Retry-After` is honoured, and a value longer than `MaxRetryAfterDelay` is treated as a
    refusal to retry rather than an instruction to block for it. A capability gap is never
    retried, since waiting will not give a transport an ability it lacks.
- **`Runax.Http.Abstractions`** — the attributes a client interface is written with and the
  contracts a transport implements. Zero package dependencies on every target framework: the BCL
  and nothing else, so neither an application nor a transport author inherits anything from
  referencing it.
  - Request attributes `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]`, `[Head]`, `[Options]`,
    taking a path template with `{placeholder}` segments; binding attributes `[Query]`,
    `[Path]`, `[Body]`, `[Header]`; client attributes `[BaseAddress]`, `[Transport]`,
    `[HttpClientContract]`.
  - `IHttpTransport`, with `TransportRequest` / `TransportResponse` and a closed `RequestContent`
    hierarchy (`BinaryContent`, `StreamedContent`, `FormContent`, `MultipartFormContent`). Two
    invariants keep a transport's job to translation only: the request URI arrives absolute and
    final, with the template substituted and the query encoded, and the body arrives already
    serialized. Transports never compose URLs and never serialize.
  - `TransportCapabilities`, plus `TransportRequest.RequiredCapabilities`, which derives what a
    request needs from the request itself so the check is mechanical rather than something each
    transport has to remember to perform.
  - `IContentSerializer` — serialization sits above the transport boundary, so the same client
    interface produces and accepts the same JSON whichever transport runs it.
  - `HttpApiException` (the server answered with an error status), `TransportException` (the
    request never completed), `TransportCapabilityException` (the transport can't do what the
    request needs), and `ApiResponse` / `ApiResponse<T>` for methods where a non-success status
    is an expected outcome rather than a fault.
- Test projects for both packages (137 tests across three target frameworks) and a native-AOT smoke
  test that CI publishes and runs. The smoke test exercises a generated client resolved from a real
  container over a source-generated JSON context, so AOT support covers the generated code rather
  than only the hand-written runtime.
- Repository scaffolding: Central Package Management (`Directory.Packages.props`), shared
  build and packaging settings for `src/` and `tests/`, multi-targeting
  (`net8.0;net9.0;net10.0`), warnings-as-errors, SourceLink and deterministic builds.
- CI on push and pull request: build, unit tests on every target framework, `dotnet format`
  verification, and a native-AOT publish smoke test.
- Release publishing to NuGet via Trusted Publishing (OIDC), triggered by a published GitHub
  release and versioned from the release tag.
- Community health files: contributing guide, code of conduct, security policy, issue and pull
  request templates, CODEOWNERS and Dependabot.

[Unreleased]: https://github.com/runax-software/runax-http/compare/v1.0.0...main
[1.0.0]: https://github.com/runax-software/runax-http/releases/tag/v1.0.0
