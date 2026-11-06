using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// A transport name that nothing is registered under is caught when transports resolve, rather
/// than on the request that happens to ask for it.
/// </summary>
/// <remarks>
/// A name has to stay a string — it can come from configuration, and several transports of one
/// type can be registered under different names — so a typo cannot be a build error. What it can
/// be is an immediate one, with the client named, instead of a 404-shaped surprise on the first
/// call to one method.
/// </remarks>
public class TransportValidationTests
{
    private static IServiceProvider Build(
        Action<IServiceCollection> register,
        string transportName = "recording")
    {
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(new RecordingTransport(transportName));
        shared.UseDefaultTransport(transportName);

        register(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_misspelled_UseTransport_names_the_client_that_asked_for_it()
    {
        var provider = Build(services =>
            services.AddRunaxClient<IRepositoryClient>().UseTransport("restsharpp"));

        var thrown = Should.Throw<UnknownTransportException>(
            () => provider.GetRequiredService<IRepositoryClient>());

        thrown.RequestedName.ShouldBe("restsharpp");

        // Without the contract in the message you are left grepping every registration in the
        // application for the typo.
        thrown.Message.ShouldContain(nameof(IRepositoryClient));
        thrown.Message.ShouldContain("recording");
    }

    [Fact]
    public void A_declared_transport_that_is_not_registered_fails_before_anything_calls_it()
    {
        // ITrafficClient declares [Transport("declared")] on one method. Previously this waited
        // until that method was called, which for a rarely used one could be a very long wait.
        var provider = Build(services => services.AddRunaxClient<ITrafficClient>());

        var thrown = Should.Throw<UnknownTransportException>(
            () => provider.GetRequiredService<ITrafficClient>());

        thrown.RequestedName.ShouldBe("declared");
        thrown.Message.ShouldContain(nameof(ITrafficClient));
        thrown.Message.ShouldContain("UseTransport");
    }

    [Fact]
    public void Registration_overriding_a_declared_transport_is_the_escape_hatch()
    {
        // The consumer of an interface that asks for a transport they do not want should not be
        // forced to install it. Overriding the declared name is how they opt out, so the override
        // has to suppress the check as well.
        var provider = Build(services =>
            services.AddRunaxClient<ITrafficClient>().UseTransport("recording"));

        Should.NotThrow(() => provider.GetRequiredService<ITrafficClient>());
    }

    [Fact]
    public void A_client_asking_for_nothing_in_particular_is_fine()
    {
        var provider = Build(services => services.AddRunaxClient<IRepositoryClient>());

        Should.NotThrow(() => provider.GetRequiredService<IRepositoryClient>());
    }

    [Fact]
    public void A_transport_registered_after_the_client_still_counts()
    {
        // Registration order is not something a consumer should have to think about: the check runs
        // when the resolver is built, which is after every registration call has run.
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();

        services.AddRunaxClient<ITrafficClient>();

        shared.AddTransport(new RecordingTransport("declared"));
        shared.UseDefaultTransport("declared");

        var provider = services.BuildServiceProvider();

        Should.NotThrow(() => provider.GetRequiredService<ITrafficClient>());
    }

    [Fact]
    public void The_generator_reports_every_name_an_interface_mentions()
    {
        // The validation can only be as good as what the generator collected, so this pins down
        // that method-level names make it into the registration and not just interface-level ones.
        GeneratedClientRegistry.TryGet(typeof(ITrafficClient), out var generated).ShouldBeTrue();

        generated!.DeclaredTransportNames.ShouldBe(["declared"]);
    }

    [Fact]
    public void An_interface_that_names_no_transport_declares_none()
    {
        GeneratedClientRegistry.TryGet(typeof(IRepositoryClient), out var generated).ShouldBeTrue();

        generated!.DeclaredTransportNames.ShouldBeEmpty();
    }

    [Fact]
    public void The_built_in_transport_name_is_available_as_a_constant()
    {
        // The preferred way to name a transport: each transport package exposes its own constant,
        // so the set stays open to anyone while callers get IntelliSense instead of a string
        // literal. An enum here would have closed the set and, worse, put it in the one package
        // every transport already references.
        HttpClientTransport.TransportName.ShouldBe("httpclient");

        // AddRunaxHttp already registers the built-in transport under that name, so no transport is
        // added here — the point is only that the constant is what you pass.
        var services = new ServiceCollection();
        services.AddRunaxHttp().UseSystemTextJson();
        services.AddRunaxClient<IRepositoryClient>()
            .UseTransport(HttpClientTransport.TransportName);

        var provider = services.BuildServiceProvider();

        Should.NotThrow(() => provider.GetRequiredService<IRepositoryClient>());
    }

    [Fact]
    public void Two_transports_cannot_share_a_name()
    {
        // A name selects which stack a request goes over, so a duplicate makes that selection
        // meaningless. Registering one under a built-in name is the easy way to do this by
        // accident, which is why it fails rather than letting one shadow the other.
        var services = new ServiceCollection();
        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(new RecordingTransport(HttpClientTransport.TransportName));

        services.AddRunaxClient<IRepositoryClient>();

        var provider = services.BuildServiceProvider();

        Should.Throw<ArgumentException>(() => provider.GetRequiredService<IRepositoryClient>())
            .Message.ShouldContain(HttpClientTransport.TransportName);
    }
}
