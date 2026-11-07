using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Sets how long a request may take when the method does not say.</summary>
    /// <param name="timeout">The timeout.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> WithTimeout(TimeSpan timeout);

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

    /// <summary>Sets the serializer for this client's bodies, overriding the application-wide one.</summary>
    /// <param name="serializer">The serializer.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseSerializer(IContentSerializer serializer);

    /// <summary>Configures anything the named helpers do not cover.</summary>
    /// <param name="configure">Applied to this client's options.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> Configure(Action<RunaxClientOptions> configure);
}
