using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.Serialization;
using Runax.Http.Transports;

namespace Runax.Http.DependencyInjection;

/// <summary>
/// Configures what every Runax.Http client in the application shares: the transports available to
/// them, which one is the default, and how bodies are serialized.
/// </summary>
public interface IRunaxHttpBuilder
{
    /// <summary>The service collection being configured.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Uses <c>System.Text.Json</c> with the given options for every client that does not
    /// configure its own.
    /// </summary>
    /// <param name="options">
    /// The JSON options. Needs a <see cref="System.Text.Json.JsonSerializerOptions.TypeInfoResolver"/>,
    /// normally a source-generated <c>JsonSerializerContext</c> — which is what keeps this path free
    /// of reflection and therefore usable under trimming and native AOT.
    /// </param>
    /// <returns>This builder.</returns>
    IRunaxHttpBuilder UseSystemTextJson(System.Text.Json.JsonSerializerOptions options);

    /// <summary>
    /// Uses <c>System.Text.Json</c> with web conventions and reflection-based type discovery.
    /// </summary>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The convenient choice, and the one that does not survive trimming or native AOT. The
    /// warning on it is deliberate: better a build-time warning than a trimmed deployment throwing
    /// on its first request.
    /// </remarks>
    [RequiresUnreferencedCode(
        "Reflection-based JSON discovers members at run time, which trimming can remove. "
        + "Use UseSystemTextJson(options) with a source-generated JsonSerializerContext instead.")]
    [RequiresDynamicCode(
        "Reflection-based JSON builds converters at run time, which native AOT cannot do. "
        + "Use UseSystemTextJson(options) with a source-generated JsonSerializerContext instead.")]
    IRunaxHttpBuilder UseSystemTextJson();

    /// <summary>Sets the serializer used by clients that do not configure their own.</summary>
    /// <param name="serializer">The serializer.</param>
    /// <returns>This builder.</returns>
    IRunaxHttpBuilder UseSerializer(IContentSerializer serializer);

    /// <summary>Sets the serializer used by clients that do not configure their own.</summary>
    /// <typeparam name="TSerializer">The serializer type, resolved from the container.</typeparam>
    /// <returns>This builder.</returns>
    IRunaxHttpBuilder UseSerializer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>()
        where TSerializer : class, IContentSerializer;

    /// <summary>
    /// Sets the transport used by clients and requests that do not name one.
    /// </summary>
    /// <param name="name">The transport name.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// Only needed when more than one transport is registered. With a single transport, that one is
    /// the default; with several and no default set, a request that names none fails rather than
    /// being sent over whichever happened to be registered first.
    /// </remarks>
    IRunaxHttpBuilder UseDefaultTransport(string name);

    /// <summary>Registers a transport instance.</summary>
    /// <param name="transport">The transport.</param>
    /// <returns>This builder.</returns>
    IRunaxHttpBuilder AddTransport(IHttpTransport transport);

    /// <summary>Registers a transport resolved from the container.</summary>
    /// <typeparam name="TTransport">The transport type.</typeparam>
    /// <returns>This builder.</returns>
    IRunaxHttpBuilder AddTransport<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>()
        where TTransport : class, IHttpTransport;

    /// <summary>Registers a transport built from the container.</summary>
    /// <param name="factory">Creates the transport.</param>
    /// <returns>This builder.</returns>
    IRunaxHttpBuilder AddTransport(Func<IServiceProvider, IHttpTransport> factory);
}
