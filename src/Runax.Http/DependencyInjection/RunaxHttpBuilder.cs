using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runax.Http.Serialization;
using Runax.Http.Transports;

namespace Runax.Http.DependencyInjection;

/// <inheritdoc />
internal sealed class RunaxHttpBuilder : IRunaxHttpBuilder
{
    private readonly RunaxHttpDefaults _defaults;

    internal RunaxHttpBuilder(IServiceCollection services, RunaxHttpDefaults defaults)
    {
        Services = services;
        _defaults = defaults;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IRunaxHttpBuilder UseSystemTextJson(System.Text.Json.JsonSerializerOptions options)
        => UseSerializer(new SystemTextJsonContentSerializer(options));

    /// <inheritdoc />
    [RequiresUnreferencedCode(
        "Reflection-based JSON discovers members at run time, which trimming can remove. "
        + "Use UseSystemTextJson(options) with a source-generated JsonSerializerContext instead.")]
    [RequiresDynamicCode(
        "Reflection-based JSON builds converters at run time, which native AOT cannot do. "
        + "Use UseSystemTextJson(options) with a source-generated JsonSerializerContext instead.")]
    public IRunaxHttpBuilder UseSystemTextJson()
        => UseSerializer(SystemTextJsonContentSerializer.CreateReflectionBased());

    /// <inheritdoc />
    public IRunaxHttpBuilder UseSerializer(IContentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        // Replace rather than Add, so calling this twice leaves one serializer registered rather
        // than two with the first one winning.
        Services.Replace(ServiceDescriptor.Singleton(serializer));

        return this;
    }

    /// <inheritdoc />
    public IRunaxHttpBuilder UseSerializer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSerializer>()
        where TSerializer : class, IContentSerializer
    {
        Services.TryAddSingleton<TSerializer>();
        Services.Replace(ServiceDescriptor.Singleton<IContentSerializer>(
            provider => provider.GetRequiredService<TSerializer>()));

        return this;
    }

    /// <inheritdoc />
    public IRunaxHttpBuilder UseDefaultTransport(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Set on the registered instance rather than through IOptions: the resolver reads this when
        // it is constructed, and that can happen after every registration call has run, so it needs
        // the final value rather than a snapshot taken at this point in the chain.
        _defaults.DefaultTransportName = name;

        return this;
    }

    /// <inheritdoc />
    public IRunaxHttpBuilder AddTransport(IHttpTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        Services.AddSingleton(transport);

        return this;
    }

    /// <inheritdoc />
    public IRunaxHttpBuilder AddTransport<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>()
        where TTransport : class, IHttpTransport
    {
        Services.AddSingleton<IHttpTransport, TTransport>();

        return this;
    }

    /// <inheritdoc />
    public IRunaxHttpBuilder AddTransport(Func<IServiceProvider, IHttpTransport> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Services.AddSingleton(factory);

        return this;
    }
}
