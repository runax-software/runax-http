using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// Which transport a request goes over, and who gets to decide.
/// </summary>
/// <remarks>
/// This is the feature the whole design exists for, so the precedence is pinned down rather than
/// left to whatever the implementation happens to do: per call beats registration beats what the
/// source declared. The person closest to the decision wins, and an interface author's
/// <c>[Transport]</c> is a suggestion rather than a constraint on whoever consumes it.
/// </remarks>
public class TransportSelectionTests
{
    private sealed record Fixture(
        IRepositoryClient Client,
        ITrafficClient Traffic,
        RecordingTransport Declared,
        RecordingTransport Registered,
        RecordingTransport Scoped);

    /// <summary>
    /// Three transports, one per level of precedence, so which one received the request is the
    /// assertion.
    /// </summary>
    private static Fixture Build(
        Action<IRunaxClientBuilder<IRepositoryClient>>? configure = null,
        Action<IRunaxClientBuilder<ITrafficClient>>? configure2 = null)
    {
        var declared = new RecordingTransport("declared", respond: _ => Responses.Json(Body));
        var registered = new RecordingTransport("registered", respond: _ => Responses.Json(Body));
        var scoped = new RecordingTransport("scoped", respond: _ => Responses.Json(Body));

        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(declared);
        shared.AddTransport(registered);
        shared.AddTransport(scoped);
        shared.UseDefaultTransport("declared");

        // Registered unconditionally: with the registration inside a null-conditional argument,
        // `configure?.Invoke(services.AddRunaxClient<T>())` never evaluates the argument when
        // configure is null, and the client is silently never registered.
        var client = services.AddRunaxClient<IRepositoryClient>();
        configure?.Invoke(client);

        var traffic = services.AddRunaxClient<ITrafficClient>();
        configure2?.Invoke(traffic);

        var provider = services.BuildServiceProvider();

        return new Fixture(
            provider.GetRequiredService<IRepositoryClient>(),
            provider.GetRequiredService<ITrafficClient>(),
            declared,
            registered,
            scoped);
    }

    private const string Body = """{"name":"n","stars":0}""";

    [Fact]
    public async Task A_method_level_Transport_attribute_selects_a_transport()
    {
        var fixture = Build();

        await fixture.Traffic.GetTrafficAsync("owner", "name");

        fixture.Declared.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Registration_overrides_what_the_source_declared()
    {
        var fixture = Build(configure2: client => client.UseTransport("registered"));

        await fixture.Traffic.GetTrafficAsync("owner", "name");

        // GetTrafficAsync carries [Transport("declared")], and it lost. Whoever deploys the
        // application knows things the interface author did not.
        fixture.Registered.Requests.Count.ShouldBe(1);
        fixture.Declared.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scope_overrides_registration()
    {
        var fixture = Build(configure2: client => client.UseTransport("registered"));

        using (TransportScope.Use("scoped"))
            await fixture.Traffic.GetTrafficAsync("owner", "name");

        fixture.Scoped.Requests.Count.ShouldBe(1);
        fixture.Registered.Requests.ShouldBeEmpty();
        fixture.Declared.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scope_applies_to_methods_that_declare_nothing()
    {
        var fixture = Build(client => client.UseTransport("registered"));

        using (TransportScope.Use("scoped"))
            await fixture.Client.GetAsync("owner", "name");

        fixture.Scoped.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Leaving_a_scope_restores_the_previous_selection()
    {
        var fixture = Build(client => client.UseTransport("registered"));

        using (TransportScope.Use("scoped"))
            await fixture.Client.GetAsync("owner", "name");

        await fixture.Client.GetAsync("owner", "name");

        fixture.Scoped.Requests.Count.ShouldBe(1);
        fixture.Registered.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Nested_scopes_restore_the_outer_one()
    {
        var fixture = Build(client => client.UseTransport("registered"));

        using (TransportScope.Use("scoped"))
        {
            using (TransportScope.Use("declared"))
                await fixture.Client.GetAsync("owner", "name");

            await fixture.Client.GetAsync("owner", "name");
        }

        fixture.Declared.Requests.Count.ShouldBe(1);
        fixture.Scoped.Requests.Count.ShouldBe(1);
        fixture.Registered.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scope_flows_across_awaits()
    {
        var fixture = Build();

        using (TransportScope.Use("scoped"))
        {
            await Task.Yield();
            await fixture.Client.GetAsync("owner", "name");
        }

        fixture.Scoped.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_scope_does_not_leak_to_a_concurrent_caller()
    {
        var fixture = Build();

        var scoped = Task.Run(async () =>
        {
            using (TransportScope.Use("scoped"))
                await fixture.Client.GetAsync("owner", "scoped");
        });

        var unscoped = Task.Run(() => fixture.Client.GetAsync("owner", "unscoped"));

        await Task.WhenAll(scoped, unscoped);

        fixture.Scoped.Requests.Count.ShouldBe(1);
        fixture.Declared.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Asking_for_an_unregistered_transport_names_the_ones_that_exist()
    {
        var fixture = Build();

        var thrown = await Should.ThrowAsync<UnknownTransportException>(async () =>
        {
            using (TransportScope.Use("restsharp"))
                await fixture.Client.GetAsync("owner", "name");
        });

        // Almost always a typo or a missing transport package, and both are answered by seeing
        // what is actually registered.
        thrown.RequestedName.ShouldBe("restsharp");
        thrown.Message.ShouldContain("declared");
        thrown.Message.ShouldContain("registered");
    }

    [Fact]
    public async Task A_single_registered_transport_is_the_default_without_being_named()
    {
        var harness = ClientHarness.Create(_ => Responses.Json(Body));

        await harness.Client.GetAsync("owner", "name");

        harness.Transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_request_needing_a_capability_the_transport_lacks_fails_before_it_is_sent()
    {
        // The recording transport declares no capabilities, and a configured timeout needs
        // PerRequestTimeout. Sending it anyway and ignoring the timeout is the quiet failure the
        // capability model exists to prevent.
        var harness = ClientHarness.Create(
            _ => Responses.Json(Body),
            configure: client => client.WithTimeout(TimeSpan.FromSeconds(5)));

        var thrown = await Should.ThrowAsync<TransportCapabilityException>(
            () => harness.Client.GetAsync("owner", "name"));

        thrown.Missing.ShouldBe(TransportCapabilities.PerRequestTimeout);
        thrown.TransportName.ShouldBe("recording");
        harness.Transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_capable_transport_receives_the_timeout()
    {
        var harness = ClientHarness.Create(
            _ => Responses.Json(Body),
            capabilities: TransportCapabilities.PerRequestTimeout,
            configure: client => client.WithTimeout(TimeSpan.FromSeconds(5)));

        await harness.Client.GetAsync("owner", "name");

        harness.Request.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
    }
}
