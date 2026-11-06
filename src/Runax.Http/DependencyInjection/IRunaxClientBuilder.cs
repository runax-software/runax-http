using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Sets the serializer for this client's bodies, overriding the application-wide one.</summary>
    /// <param name="serializer">The serializer.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> UseSerializer(IContentSerializer serializer);

    /// <summary>Configures anything the named helpers do not cover.</summary>
    /// <param name="configure">Applied to this client's options.</param>
    /// <returns>This builder.</returns>
    IRunaxClientBuilder<TContract> Configure(Action<RunaxClientOptions> configure);
}
