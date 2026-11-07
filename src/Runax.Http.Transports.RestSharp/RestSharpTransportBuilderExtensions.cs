using Runax.Http.DependencyInjection;
using Runax.Http.Transports;

namespace Runax.Http;

/// <summary>Registers the RestSharp transport.</summary>
public static class RestSharpTransportBuilderExtensions
{
    /// <summary>
    /// Adds the RestSharp transport, selectable as <c>restsharp</c>.
    /// </summary>
    /// <param name="builder">The Runax.Http builder.</param>
    /// <param name="configure">Optional configuration for the underlying client.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Registering more than one is how you get a client per proxy: give each a different
    /// <see cref="RestSharpTransportOptions.Name"/> and select between them with
    /// <c>UseTransport(...)</c> or <c>TransportScope.Use(...)</c>.
    /// </remarks>
    public static IRunaxHttpBuilder AddRestSharpTransport(
        this IRunaxHttpBuilder builder,
        Action<RestSharpTransportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new RestSharpTransportOptions();
        configure?.Invoke(options);

        // Constructed now rather than from a factory, so a configuration mistake surfaces at
        // registration instead of on the first request.
        return builder.AddTransport(new RestSharpTransport(options));
    }
}
