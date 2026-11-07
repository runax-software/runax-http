# Retries

Off until you ask for them. A client that retries without being told to can turn one slow
dependency into an outage.

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .WithRetry(3);                   // one try, two retries
```

Full configuration:

```csharp
services.AddRunaxClient<IRepositoryClient>()
        .WithRetry(options =>
        {
            options.MaxAttempts = 5;
            options.Delay = TimeSpan.FromMilliseconds(200);
            options.MaxDelay = TimeSpan.FromSeconds(10);
            options.Backoff = BackoffStrategy.Exponential;   // or Linear, Constant
            options.UseJitter = true;
            options.RespectRetryAfter = true;
            options.MaxRetryAfterDelay = TimeSpan.FromMinutes(1);
            options.RetryTransportFailures = true;
            options.RetryableStatusCodes.Add(500);
            options.RetryNonIdempotentMethods = true;        // read below first
        });
```

| Option | Default |
| --- | --- |
| `MaxAttempts` | `1` — retrying off |
| `Delay` | 500 ms |
| `MaxDelay` | 30 s |
| `Backoff` | `Exponential` |
| `UseJitter` | `true` |
| `RespectRetryAfter` | `true` |
| `MaxRetryAfterDelay` | 1 min |
| `RetryNonIdempotentMethods` | `false` |
| `RetryTransportFailures` | `true` |
| `RetryableStatusCodes` | 408, 429, 502, 503, 504 |

`MaxAttempts` counts the first attempt, so `3` means one try and two retries. Setting anything else
without raising it above one retries nothing.

## Three defaults that are safety decisions

These are not taste. Change them deliberately.

### Idempotent methods only

`GET`, `HEAD`, `OPTIONS`, `PUT` and `DELETE` are retried. `POST` and `PATCH` are not.

A retried `POST` that timed out *after* the server committed it creates the thing twice, and the
client has no way to know which happened. Turn `RetryNonIdempotentMethods` on only for an endpoint
with an idempotency key or a genuinely idempotent handler.

### A streamed body is never retried

Whatever the settings say. The first attempt consumed the stream, so a second would send a
truncated request — and buffering it instead would mean holding an upload of unknown size in memory
on the off chance of a retry. Neither is acceptable silently, so the request is sent once and its
failure surfaces normally.

### 500 is not retryable

408, 429, 502, 503 and 504 are. A plain Internal Server Error is usually a bug in the request or
the handler, so repeating it multiplies the log noise without changing the outcome. Add it if your
server is known to use 500 for transient faults.

## Backoff

| Strategy | Waits |
| --- | --- |
| `Constant` | `Delay` every time |
| `Linear` | `Delay`, `2×Delay`, `3×Delay` |
| `Exponential` | `Delay`, `2×Delay`, `4×Delay` |

Capped at `MaxDelay`, because exponential backoff over enough attempts becomes a wait nobody
intended — eight attempts from one second is over two minutes.

**Jitter is on by default**, spreading the wait over `[delay/2, delay]`. Without it, every client
that failed at the same moment retries at the same moment, and a service recovering from a blip
gets hit by a synchronized wave — the problem the retry was meant to ride out, rebuilt on a timer.

## Retry-After

Honoured when present, in both legal forms: a number of seconds, or an HTTP date. It overrides the
computed backoff and is taken as given — not jittered, not capped by `MaxDelay`. The server named a
number, and second-guessing it on a 429 is how a rate limit becomes a ban.

A value longer than `MaxRetryAfterDelay` is treated as a **refusal to retry** rather than an
instruction to block for it: the response is returned as the answer. An unparseable value is
ignored and the backoff curve decides.

## What is retried

- **Transport failures** — DNS failure, connection refused, TLS failure, timeout. This is the case
  retrying actually exists for: no status code was ever received. Controlled by
  `RetryTransportFailures`.
- **Retryable status codes** — the set above.

Not retried: a capability gap, a cancelled request, or anything else thrown before the request
reached the transport.

## Where it sits

Above the transport boundary. Every transport gets the same retry behaviour, none of them
implements it, and swapping a transport does not change your retry semantics.

`DelegatingHandler` would have been the obvious place and only exists in `HttpClient`-land, so
anything built on it could not be reused for the other transports. See
[Architecture](architecture.md).

## One flaky endpoint

`WithRetry` is a client-wide policy. For the single endpoint that needs its own, declare it:

```csharp
[Get("/reports/{id}")]
[Retry(5, DelayMilliseconds = 250)]
Task<Report> GetReportAsync(int id, CancellationToken ct = default);
```

It **refines** the client's settings rather than replacing them: whatever the client was registered
with — backoff curve, jitter, which status codes count, `Retry-After` handling — still applies, and
only what the attribute names changes.

| Property | Default | |
| --- | --- | --- |
| *(constructor)* | — | Total attempts including the first |
| `DelayMilliseconds` | inherit | The base delay. Milliseconds because a `TimeSpan` cannot be an attribute argument |
| `AllowNonIdempotent` | `false` | Whether a `POST` or `PATCH` may be retried |

`[Retry(1)]` is meaningful rather than a mistake: it says *this method must not be retried* even
though its client is configured to, which is how you exempt one dangerous call from a client-wide
policy.

A declaration also turns retrying on by itself — you do not need `WithRetry` first. If you did, the
attribute would do nothing in the common case, since retrying is off by default.

### Precedence

**A method's `[Retry]` wins over the client's attempt count. `WithoutRetries()` wins over
everything.**

```csharp
services.AddRunaxClient<IReportClient>().WithRetry(2);     // client-wide default
// GetReportAsync still gets 5, from its own [Retry(5)]

services.AddRunaxClient<IReportClient>().WithoutRetries(); // nothing retries, period
```

That is the opposite of how [`[Transport]`](transports.md#choosing-one) resolves, where
registration wins — and the difference is the point. A transport is a *competing* claim about one
fact: which stack sends the request. One of the two has to lose, and the operator knows the
deployment. A retry count is a *refinement*: "how should this endpoint behave" narrows "how should
this client behave by default", so there is no tie to break and the more specific statement simply
applies. The same relationship already holds for a per-request
[timeout](configuration.md) and for a method's `[Header]` over a client-wide default header.

`WithoutRetries()` exists because the operator still needs the last word. A client that cannot be
stopped from retrying is a liability in the middle of an incident. It is **not** the same as
`WithRetry(1)`, which only says the client has no policy of its own and leaves a declaration free
to supply one.

### The safety defaults still hold

A `[Retry]` is not a way around them.

A **streamed body is never retried**, whatever the attribute says — the first attempt consumed the
stream, so a second would send a truncated request.

A **non-idempotent method is not retried** unless `AllowNonIdempotent = true`. That lives on the
attribute as well as at registration because the interface author is the one who knows whether a
given endpoint has an idempotency key; the client-wide `RetryNonIdempotentMethods` is the blunter
instrument, opting in every method at once. A `[Retry]` on a `POST` *without* it is
[`RXH020`](diagnostics.md#rxh020) — a warning, because the attribute would otherwise do nothing at
all and nothing else in the build would say so.

## Next

- [Capabilities](capabilities.md) — why a capability gap is not retried
- [Configuration](configuration.md) — everything else on a client
