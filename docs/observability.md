# Observability

Spans and metrics on every request, under the OpenTelemetry HTTP client semantic conventions, with
the active transport as a tag.

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .UseTelemetry();
```

That is the whole setup on the client side. Nothing is on by default, and nothing is exported until
you point a collector at it.

## No dependency on OpenTelemetry

The package does not reference OpenTelemetry, and it never will. Spans go to
`System.Diagnostics.Activity` and measurements to a `System.Diagnostics.Metrics.Meter` — both in the
base class library, both designed as the vendor-neutral seam that collectors subscribe to. A library
that took the SDK as a dependency would make every consumer take it too, including the ones who
export to something else entirely or to nothing at all.

Subscribe by name:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(RunaxHttpTelemetry.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(RunaxHttpTelemetry.MeterName));
```

Both names are `"Runax.Http"`. They are exposed as constants rather than documented strings so a
rename cannot silently unsubscribe you.

Until something subscribes, both are inert: `ActivitySource.StartActivity` returns `null` with no
registered listener, and the histogram skips a measurement when its meter has no subscriber. An
unobserved client pays for a null check, which is why this is safe to leave switched on in a library
whose consumers may have no collector.

## One span per attempt

A call that retries twice produces three spans, not one.

That is what the conventions describe — a resend carries `http.request.resend_count` — and it is the
only shape that can answer the question retries create. A call that took 4.2 seconds because the
server was slow and a call that took 4.2 seconds because two tries failed and the backoff was waited
out are different problems with different fixes. One span covering the whole call reports 4.2 seconds
for both.

The attempts are tied together by their parent span, and by the correlation id if that stage is on.

## Span attributes

| Attribute | Example | Notes |
| --- | --- | --- |
| `http.request.method` | `GET` | Always known — the method comes from an enum, so `_OTHER` never applies |
| `url.full` | `https://api.example.com/v3/repos/runax/http?state=open` | Credentials stripped; query optional, see below |
| `url.scheme` | `https` | |
| `url.template` | `/repos/{owner}/{name}` | The low-cardinality endpoint name, taken from the contract |
| `server.address` | `api.example.com` | |
| `server.port` | `8443` | Only when it is not the default for the scheme |
| `http.response.status_code` | `200` | |
| `http.request.resend_count` | `1` | Absent on a first attempt, not zero |
| `error.type` | `503`, `System.Net.Sockets.SocketException` | The status for an HTTP error, the exception type for a failure that never got one |
| `runax.http.transport` | `httpclient` | The transport that answered |
| `runax.http.client` | `IRepositoryClient` | The contract that issued it |
| `runax.http.correlation_id` | `4bf92f…` | Present when `UseCorrelation()` is on |

The span is named `{method} {url.template}` — `GET /repos/{owner}/{name}` — and its kind is
`Client`. The name deliberately does not contain the URL: a name with an id in it is one distinct
operation per id, and a backend that groups by operation name ends up with a million groups of one.

**4xx and 5xx set the span status to `Error`.** The server may consider a 404 a perfectly good
answer; the caller that asked for the thing did not get it, and for a client span the conventions
take the caller's side.

### Why the conventional names and not nicer ones

Because conformance *is* the feature. A dashboard, alert or exemplar query built against
`HttpClient` keeps working when a call starts going through a Runax.Http client, and the attributes a
backend knows how to group by are the ones it was told to expect. `http.method` instead of
`http.request.method` is indistinguishable from no instrumentation at all to anything consuming it.

The one addition is the transport, and it is namespaced under `runax.` for the same reason: inventing
an unprefixed attribute in someone else's namespace is how a convention rots.

## Metrics

One instrument: `http.client.request.duration`, a histogram, **in seconds**.

Seconds because the convention says seconds. A histogram whose unit differs from every other HTTP
duration in the same backend cannot be compared with them, and comparison is most of what a duration
is for.

Tags: `http.request.method`, `url.scheme`, `server.address`, `server.port`, `url.template`,
`http.response.status_code`, `error.type`, `runax.http.transport`, `runax.http.client`.

**`url.full` is not among them, and neither is the correlation id.** Every distinct id in a path
would become its own time series, and a metric backend charges for those by the million. High
cardinality belongs on a span, where it is one record rather than one series.

A failed call is still measured. A p99 that improves because slow calls started failing rather than
completing is a graph that lies by omission, so failures are recorded with `error.type` set and
remain separable from successes.

## Where the stage sits

Outermost — `Order` 50, outside correlation's 100.

Two reasons, and the second is a correctness constraint rather than a preference:

- The span has to enclose the time the other stages spend. An auth stage fetching a token off a cold
  cache is part of how long the call took, and a span that starts after it under-reports.
- The correlation stage derives its id from `Activity.Current`. Start the span after correlation runs
  and the `traceparent` header reaching the server names a span that does not exist — which breaks
  the join between the client trace and the server's, silently, because both halves look fine on
  their own.

With both stages on, the `traceparent` the server receives carries the trace id of the span the
client actually recorded, which is what makes the two ends one trace.

## The query string

Recorded by default, matching what the [logging stage](pipeline.md#logging) does with the same
information — one library should not take two positions on it.

Turn it off for an API that puts a key or a token in the query:

```csharp
.UseTelemetry(o => o.RecordQueryString = false)
```

A trace is stored and searchable for as long as your retention allows, which makes it a worse place
for a secret than a log line.

**Credentials in the URL are stripped unconditionally** and that is not configurable. The conventions
require it, and a password in a `url.full` attribute is a password in a tracing backend's search
index.

Request and response bodies are never recorded, for the reasons the
[logging stage](pipeline.md#what-it-will-not-log-and-why-that-is-not-configurable) sets out. Span
attributes persist in a backend exactly the way log lines do.

## Options

| Option | Default | What it does |
| --- | --- | --- |
| `Tracing` | `true` | Start a span per attempt |
| `Metrics` | `true` | Record the duration histogram |
| `RecordQueryString` | `true` | Include the query in `url.full` |
| `RecordClientName` | `true` | Tag `runax.http.client` |

Tracing and metrics are independently disableable, because the costs are not the same: a span per
request is cheap to emit and expensive to store, and a histogram is the reverse.

## Native AOT

`ActivitySource` and `Meter` need no reflection, so the stage is AOT-safe with nothing to configure.
The AOT smoke test publishes with this stage attached and asserts zero trim warnings — see
[Native AOT & trimming](aot.md).

## Next

- [The request pipeline](pipeline.md) — the stages this one wraps
- [Retries](retries.md) — why there is a span per attempt
- [Transports](transports.md) — what `runax.http.transport` names
