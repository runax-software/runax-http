using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runax.Http.Proxy;
using Runax.Http.Serialization;
using Runax.Http.Transports;

namespace Runax.Http.DependencyInjection;

/// <summary>Registers Runax.Http and its clients with a service collection.</summary>
public static class RunaxHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared Runax.Http services: the JSON serializer, the transport resolver, and
    /// the built-in <see cref="HttpClientTransport"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>A builder for configuring serialization and transports.</returns>
    /// <remarks>
    /// Calling this is optional — <see cref="AddRunaxClient{TContract}"/> does it for you — and
    /// safe to repeat. Call it directly when you want to change a shared default, such as
    /// registering a second transport or swapping the serializer.
    /// </remarks>
    public static IRunaxHttpBuilder AddRunaxHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var defaults = GetOrAddDefaults(services, out var firstCall);

        // No serializer is registered here on purpose. The convenient one discovers types by
        // reflection, which does not survive trimming or native AOT, and registering it silently
        // would mean a library that advertises AOT support quietly opting every application out of
        // it. Choosing it is one call — UseSystemTextJson() — and that call carries the warning.
        //
        // The default transport comes from the BCL, so registering it here costs a consumer nothing
        // they did not already have. A transport package adds itself alongside it.
        //
        // Guarded on the first call rather than with TryAddEnumerable, which rejects a
        // factory-registered descriptor outright: it de-duplicates on implementation type, and a
        // factory's implementation type is the service type, so it cannot tell two factories apart.
        // Registering it twice would mean two transports claiming the name "httpclient", which the
        // resolver rejects — a confusing failure a long way from its cause.
        if (firstCall)
        {
            services.AddHttpClient(HttpClientTransport.TransportName);
            services.AddSingleton<IHttpTransport>(provider =>
                new HttpClientTransport(
                    provider.GetRequiredService<IHttpClientFactory>()
                        .CreateClient(HttpClientTransport.TransportName)));
        }

        services.TryAddSingleton<IHttpTransportResolver>(provider =>
        {
            var settings = provider.GetRequiredService<RunaxHttpDefaults>();

            var resolver = new HttpTransportResolver(
                provider.GetServices<IHttpTransport>(),
                settings.DefaultTransportName);

            // Checked here because this is the first moment the full picture exists: every
            // client registered, every transport package added, every UseTransport(...) applied.
            // Without it, a misspelled name waits until a request actually asks for it — and for a
            // method-level [Transport] on a rarely used method, that can be a long wait.
            ValidateConfiguredTransports(settings, resolver);

            return resolver;
        });

        return new RunaxHttpBuilder(services, defaults);
    }

    /// <summary>
    /// Finds the shared defaults already in the collection, or adds them. Registered as an instance
    /// so repeated <see cref="AddRunaxHttp"/> calls configure the same object instead of the last
    /// one silently winning.
    /// </summary>
    private static RunaxHttpDefaults GetOrAddDefaults(IServiceCollection services, out bool firstCall)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(RunaxHttpDefaults)
                && descriptor.ImplementationInstance is RunaxHttpDefaults existing)
            {
                firstCall = false;
                return existing;
            }
        }

        var defaults = new RunaxHttpDefaults();
        services.AddSingleton(defaults);
        firstCall = true;

        return defaults;
    }

    /// <summary>
    /// Registers a client interface, so it can be injected and its generated implementation used.
    /// </summary>
    /// <typeparam name="TContract">
    /// The client interface. Needs a generated implementation, which means the project declaring it
    /// references <c>Runax.Http</c> and the interface carries request attributes.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>A builder for configuring this client.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TContract"/> is not an interface, or no implementation was generated
    /// for it.
    /// </exception>
    public static IRunaxClientBuilder<TContract> AddRunaxClient<TContract>(this IServiceCollection services)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var contract = RequireInterface<TContract>();

        if (!TryFindGenerated(contract, out var generated))
            throw new InvalidOperationException(
                $"No generated implementation was found for {contract}. Check that the project declaring it "
                + "references the Runax.Http package — Runax.Http.Abstractions carries the attributes but not "
                + "the generator — and that the interface has at least one method marked with a request "
                + "attribute such as [Get] or [Post]. If the interface is compiled without the generator on "
                + "purpose, AddRunaxClientWithProxyFallback<T>() implements it at run time instead.");

        return Register<TContract>(services, generated);
    }

    /// <summary>
    /// Registers a client interface, using its generated implementation when there is one and a
    /// runtime proxy built by reflection when there is not.
    /// </summary>
    /// <typeparam name="TContract">The client interface.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>A builder for configuring this client.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TContract"/> is not an interface, or it has no generated implementation
    /// and cannot be proxied — a mismatched placeholder, an unbound parameter, a return type that is
    /// not <c>Task</c>-shaped. Every problem is listed, under the generator's <c>RXH</c> ids.
    /// </exception>
    /// <remarks>
    /// <para>
    /// For an interface the generator never saw: one compiled in a project that references only
    /// <c>Runax.Http.Abstractions</c>, loaded from a plugin, or written in a language the generator
    /// does not run for. Everything after the request is built — transports, retries, the pipeline,
    /// response mapping — is the same code the generated path uses, and the request it builds
    /// follows the generator's rules, so the two produce identical requests from one interface.
    /// </para>
    /// <para>
    /// What it costs: the interface is read by reflection when the client is registered, the
    /// implementation type is emitted when it is first resolved, and each call goes through a
    /// <see cref="System.Reflection.DispatchProxy"/>. Mistakes the generator reports as build errors
    /// surface here at registration instead. And none of it survives trimming or native AOT, which
    /// is why this is a separate method that warns, rather than something
    /// <see cref="AddRunaxClient{TContract}"/> does quietly when it cannot find a generated client.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(ProxyContract.TrimmingMessage)]
    [RequiresDynamicCode(ProxyContract.AotMessage)]
    public static IRunaxClientBuilder<TContract> AddRunaxClientWithProxyFallback<TContract>(
        this IServiceCollection services)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var contract = RequireInterface<TContract>();

        // Generated first: it is the same request either way, without the reflection, so a proxy is
        // only ever what happens when there is no alternative.
        var client = TryFindGenerated(contract, out var generated)
            ? generated
            : ProxyContract.For(contract).Description;

        return Register<TContract>(services, client);
    }

    private static Type RequireInterface<TContract>()
    {
        var contract = typeof(TContract);

        if (!contract.IsInterface)
            throw new InvalidOperationException(
                $"{contract} is not an interface. Runax.Http clients are declared as interfaces so the "
                + "generator has something to implement.");

        return contract;
    }

    private static bool TryFindGenerated(Type contract, [NotNullWhen(true)] out GeneratedClient? generated)
    {
        GeneratedClientRegistry.EnsureInitialized(contract);

        return GeneratedClientRegistry.TryGet(contract, out generated) && generated is not null;
    }

    /// <summary>
    /// Registers a client from its description, whichever path produced it.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so tests can register a proxy for a contract that also has a
    /// generated implementation, and compare the requests the two build.
    /// </remarks>
    internal static IRunaxClientBuilder<TContract> Register<TContract>(
        IServiceCollection services,
        GeneratedClient generated)
        where TContract : class
    {
        services.AddRunaxHttp();

        var contract = typeof(TContract);
        var options = BuildOptions(contract, generated);

        // Held as its own singleton so the builder can keep mutating the same options object after
        // this method returns, and the executor still sees the final state at resolution time.
        services.AddSingleton(new RunaxClientRegistration<TContract>(options));

        GetOrAddDefaults(services, out _).Clients.Add(
            new RegisteredClient(contract, options, generated.DeclaredTransportNames));

        services.TryAddSingleton(provider =>
        {
            var registration = provider.GetRequiredService<RunaxClientRegistration<TContract>>();

            var serializer = registration.Options.Serializer
                             ?? provider.GetService<IContentSerializer>()
                             ?? throw new InvalidOperationException(
                                 $"No content serializer is registered, so {contract} cannot read or write "
                                 + "bodies. Call AddRunaxHttp().UseSystemTextJson() for the reflection-based "
                                 + "default, pass JSON options with a source-generated context to keep "
                                 + "trimming and AOT working, or set one for this client with UseSerializer(...).");

            var middleware = registration.Options.MiddlewareFactories
                .Select(factory => factory(provider))
                .ToList();

            var executor = new HttpContractExecutor(
                registration.Options,
                provider.GetRequiredService<IHttpTransportResolver>(),
                serializer,
                middleware);

            return (TContract)generated.Factory(executor);
        });

        return new RunaxClientBuilder<TContract>(services, options);
    }

    /// <summary>
    /// Fails if a client asks for a transport nothing is registered under.
    /// </summary>
    /// <remarks>
    /// The message names the client, because "no transport named 'restsharpp'" on its own leaves
    /// you grepping for the typo across every registration in the application.
    /// </remarks>
    private static void ValidateConfiguredTransports(
        RunaxHttpDefaults settings,
        HttpTransportResolver resolver)
    {
        foreach (var client in settings.Clients)
        {
            // The registered name first: it is the one someone just typed, and it overrides
            // anything the source declared, so a typo here makes the declared names irrelevant.
            if (client.Options.TransportName is { } configured
                && !resolver.TryResolve(configured, out _))
            {
                throw new UnknownTransportException(
                    $"{client.Contract} is registered with UseTransport(\"{configured}\"), but no transport "
                    + $"is registered under that name. Registered transports: "
                    + $"{Describe(resolver)}.")
                {
                    RequestedName = configured,
                    AvailableNames = resolver.Names
                };
            }

            // A declared name that registration has overridden is never used, so checking it would
            // be noise — and worse, it would make the override useless as an escape hatch for a
            // consumer who does not want the transport an interface asks for.
            if (client.Options.TransportName is not null)
                continue;

            foreach (var declared in client.DeclaredTransportNames)
            {
                if (!resolver.TryResolve(declared, out _))
                {
                    throw new UnknownTransportException(
                        $"{client.Contract} declares [Transport(\"{declared}\")], but no transport is "
                        + $"registered under that name. Either register it, or override it with "
                        + $"UseTransport(...) when registering the client. Registered transports: "
                        + $"{Describe(resolver)}.")
                    {
                        RequestedName = declared,
                        AvailableNames = resolver.Names
                    };
                }
            }
        }
    }

    private static string Describe(HttpTransportResolver resolver)
        => resolver.Names.Count == 0 ? "none" : string.Join(", ", resolver.Names);

    /// <summary>
    /// Seeds options from what the interface declared, so attributes are the default and
    /// registration is the override rather than the only place anything can be configured.
    /// </summary>
    private static RunaxClientOptions BuildOptions(Type contract, GeneratedClient generated)
    {
        var options = new RunaxClientOptions { TransportName = generated.TransportName };

        if (generated.BaseAddress is { } declared)
        {
            if (!Uri.TryCreate(declared, UriKind.Absolute, out var parsed))
                throw new InvalidOperationException(
                    $"[BaseAddress(\"{declared}\")] on {contract} is not an absolute URI.");

            options.BaseAddress = parsed;
        }

        foreach (var (name, value) in generated.DefaultHeaders)
            options.DefaultHeaders[name] = value;

        return options;
    }
}
