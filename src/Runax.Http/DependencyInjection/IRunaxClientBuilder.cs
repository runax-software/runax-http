using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runax.Http.Pipeline;
using Runax.Http.Retry;
using Runax.Http.Serialization;

namespace Runax.Http.DependencyInjection;

/// <summary>Configures one registered client interface.</summary>
/// <typeparam name="TContract">The client interface.</typeparam>
public interface IRunaxClientBuilder<TContract>
    where TContract : class
{
    /// <summary>The service collection being configured.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Sets the address request paths are resolved against, overriding
    /// <see cref="BaseAddressAttribute"/> on the interface.
    /// </summary>
    /// <param name="baseAddress">An absolute address.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// A request path appends to this address's path rather than replacing it, so a base of
    /// <c>https://host/api/v2</c> with a path of <c>/users</c> gives
    /// <c>https://host/api/v2/users</c>.
    /// </remarks>
    IRunaxClientBuilder<TContract> WithBaseAddress(Uri baseAddress);

    /// <inheritdoc cref="WithBaseAddress(Uri)" />
    IRunaxClientBuilder<TContract> WithBaseAddress(string baseAddress);

    /// <summary>
    /// Sets the transport this client's requests go over, overriding
    /// <see cref="TransportAttribute"/> on the interface or its methods.
    /// </summary>
    /// <param name="name">The transport name.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Still overridden per call by <c>TransportScope.Use(...)</c>, so this is the deployment-time
    /// choice rather than the final word.
    /// </remarks>
    IRunaxClientBuilder<TContract> UseTransport(string name);

    /// <summary>Sets how long a single attempt may take when the method does not say.</summary>
    /// <param name="timeout">The per-attempt timeout.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Per attempt. With retries on, the caller waits this multiplied by the attempt count plus
    /// the backoff — see <see cref="WithTotalTimeout"/> for the budget that bounds the call.
    /// </remarks>
    IRunaxClientBuilder<TContract> WithTimeout(TimeSpan timeout);

    /// <summary>
    /// Sets how long the whole call may take, including every retry and the waits between them.
    /// </summary>
    /// <param name="timeout">The total budget.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The budget a caller actually experiences. Worth setting on any client with retries on: a
    /// per-attempt timeout bounds an attempt, and retries multiply it with backoff on top, so a
    /// client can block for minutes with nothing in its configuration looking wrong. Exceeding it
    /// throws <see cref="TransportException"/>, not a cancellation.
    /// </remarks>
    IRunaxClientBuilder<TContract> WithTotalTimeout(TimeSpan timeout);

    /// <summary>Adds a header sent with every request from this client.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> WithDefaultHeader(string name, string value);

    /// <summary>
    /// Turns on retrying, with everything else left at its default.
    /// </summary>
    /// <param name="maxAttempts">
    /// Total attempts including the first, so <c>3</c> means one try and two retries. Must be at
    /// least one; one means no retrying.
    /// </param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Retrying is off until you call this. The defaults it turns on are deliberately cautious:
    /// exponential backoff with jitter, <c>Retry-After</c> honoured, transient status codes only,
    /// and <em>idempotent methods only</em> — a retried <c>POST</c> that timed out after the
    /// server committed it creates the thing twice, so that needs asking for explicitly.
    /// </remarks>
    IRunaxClientBuilder<TContract> WithRetry(int maxAttempts);

    /// <summary>Configures retrying in full.</summary>
    /// <param name="configure">Applied to this client's retry settings.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Set <see cref="RetryOptions.MaxAttempts"/> above one, or nothing is retried however the
    /// rest is configured.
    /// </remarks>
    IRunaxClientBuilder<TContract> WithRetry(Action<RetryOptions> configure);

    /// <summary>
    /// Turns retrying off for this client, and ignores every <see cref="RetryAttribute"/> its
    /// methods declare.
    /// </summary>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The operator's last word. A method's <c>[Retry]</c> normally refines these settings and wins
    /// on attempt count — right when an interface author knows an endpoint is flaky, wrong in the
    /// middle of an incident when whoever is holding the pager needs a client that provably stops.
    /// <para>
    /// Not the same as <c>WithRetry(1)</c>, which only says this client has no retry policy of its
    /// own and leaves a declaration free to supply one.
    /// </para>
    /// </remarks>
    IRunaxClientBuilder<TContract> WithoutRetries();

    /// <summary>Sets the serializer for this client's bodies, overriding the application-wide one.</summary>
    /// <param name="serializer">The serializer.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseSerializer(IContentSerializer serializer);

    /// <summary>Adds a pipeline stage, resolved from the container when the client is built.</summary>
    /// <param name="factory">Creates the stage.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Where it runs is decided by <see cref="IHttpMiddleware.Order"/>, not by where this call sits
    /// in the chain.
    /// </remarks>
    IRunaxClientBuilder<TContract> Use(Func<IServiceProvider, IHttpMiddleware> factory);

    /// <summary>Adds a pipeline stage.</summary>
    /// <param name="middleware">The stage. Must be thread-safe; one instance serves every call.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> Use(IHttpMiddleware middleware);

    /// <summary>
    /// Logs each attempt through an <see cref="ILogger"/> resolved for this contract.
    /// </summary>
    /// <param name="configure">Adjusts what is written.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Bodies are never logged and header values are not logged unless named — see
    /// <see cref="HttpLoggingOptions"/> for why those are not switches.
    /// </remarks>
    IRunaxClientBuilder<TContract> UseLogging(Action<HttpLoggingOptions>? configure = null);

    /// <summary>Sends a bearer token, re-read on every attempt.</summary>
    /// <param name="tokens">Supplies the token.</param>
    /// <param name="headerName">The header to write. Defaults to <c>Authorization</c>.</param>
    /// <param name="scheme">
    /// The scheme to prepend. Pass <see langword="null"/> for an <c>X-Api-Key</c>-style header that
    /// carries the value alone.
    /// </param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseBearerToken(
        IAccessTokenProvider tokens,
        string headerName = AuthenticationMiddleware.DefaultHeaderName,
        string? scheme = AuthenticationMiddleware.DefaultScheme);

    /// <summary>Sends a bearer token from a lambda, re-read on every attempt.</summary>
    /// <param name="token">
    /// Produces the token. Called per attempt, so a refresh between a failure and its retry is
    /// picked up — which means this should be a cache read rather than anything that blocks.
    /// </param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseBearerToken(Func<string?> token);

    /// <summary>Sends a bearer token fetched asynchronously, re-read on every attempt.</summary>
    /// <param name="token">Produces the token.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseBearerToken(Func<CancellationToken, ValueTask<string?>> token);

    /// <summary>
    /// Gives each call an id, keeps it stable across retries, and sends it to the server.
    /// </summary>
    /// <param name="configure">Adjusts the header name and trace-context propagation.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseCorrelation(Action<CorrelationOptions>? configure = null);

    /// <summary>
    /// Records an OpenTelemetry span and a request-duration measurement per attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitting only. Nothing here collects or exports anything, and the package takes no
    /// dependency on OpenTelemetry — the span goes to <see cref="System.Diagnostics.Activity"/> and
    /// the measurement to a <see cref="System.Diagnostics.Metrics.Meter"/>, both of which are in
    /// the base library. Point a collector at them by name:
    /// </para>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(t => t.AddSource(RunaxHttpTelemetry.ActivitySourceName))
    ///     .WithMetrics(m => m.AddMeter(RunaxHttpTelemetry.MeterName));
    /// </code>
    /// <para>
    /// Until something subscribes, both are inert — so this is safe to leave on in a library whose
    /// consumers may not have a collector at all.
    /// </para>
    /// </remarks>
    /// <param name="configure">Turns tracing or metrics off, and adjusts what is recorded.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseTelemetry(Action<TelemetryOptions>? configure = null);

    /// <summary>Configures anything the named helpers do not cover.</summary>
    /// <param name="configure">Applied to this client's options.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> Configure(Action<RunaxClientOptions> configure);
}
