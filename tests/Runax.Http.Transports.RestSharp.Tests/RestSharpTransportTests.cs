using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Transports;

namespace Runax.Http.Transports.RestSharp.Tests;

/// <summary>What is specific to this transport, on top of the shared conformance suite.</summary>
public class RestSharpTransportTests
{
    [Fact]
    public void It_is_selected_by_the_name_on_its_own_constant()
    {
        using var transport = new RestSharpTransport();

        RestSharpTransport.TransportName.ShouldBe("restsharp");
        transport.Name.ShouldBe(RestSharpTransport.TransportName);
    }

    [Fact]
    public void It_does_not_claim_streaming_it_cannot_do()
    {
        // The concrete payoff of the capability model, and a genuine difference from the HttpClient
        // transport: RestSharp's ExecuteAsync reads the whole response before returning, and its
        // body parameters take a value rather than a stream.
        RestSharpTransport.DefaultCapabilities
            .HasFlag(TransportCapabilities.RequestStreaming).ShouldBeFalse();

        RestSharpTransport.DefaultCapabilities
            .HasFlag(TransportCapabilities.ResponseStreaming).ShouldBeFalse();
    }

    [Fact]
    public void It_does_not_claim_a_per_request_proxy()
    {
        // A proxy belongs to RestClientOptions, so it is per client — the same constraint as
        // HttpClient, for the same reason.
        RestSharpTransport.DefaultCapabilities
            .HasFlag(TransportCapabilities.PerRequestProxy).ShouldBeFalse();
    }

    [Fact]
    public void Configuration_dependent_capabilities_follow_the_configuration()
    {
        using var plain = new RestSharpTransport();

        plain.Capabilities.HasFlag(TransportCapabilities.AutomaticDecompression).ShouldBeFalse();
        plain.Capabilities.HasFlag(TransportCapabilities.Cookies).ShouldBeFalse();

        using var configured = new RestSharpTransport(new RestSharpTransportOptions
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            CookieContainer = new System.Net.CookieContainer()
        });

        // Declared because they are now true of this instance, not because the type could in
        // principle do them.
        configured.Capabilities.HasFlag(TransportCapabilities.AutomaticDecompression).ShouldBeTrue();
        configured.Capabilities.HasFlag(TransportCapabilities.Cookies).ShouldBeTrue();
    }

    [Fact]
    public void Turning_off_redirects_drops_the_capability()
    {
        using var transport = new RestSharpTransport(new RestSharpTransportOptions
        {
            FollowRedirects = false
        });

        transport.Capabilities.HasFlag(TransportCapabilities.AutomaticRedirect).ShouldBeFalse();
    }

    [Fact]
    public void It_can_be_registered_more_than_once_under_different_names()
    {
        // The reason a transport is identified by name rather than by type: a client per proxy is
        // several instances of one transport, and only a name can tell them apart.
        var services = new ServiceCollection();

        services.AddRunaxHttp()
            .AddRestSharpTransport(options => options.Name = "restsharp-eu")
            .AddRestSharpTransport(options => options.Name = "restsharp-us");

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpTransportResolver>();

        resolver.Names.ShouldContain("restsharp-eu");
        resolver.Names.ShouldContain("restsharp-us");
        resolver.Resolve("restsharp-eu").ShouldNotBeSameAs(resolver.Resolve("restsharp-us"));
    }

    [Fact]
    public void AddRestSharpTransport_registers_it_alongside_the_built_in_one()
    {
        var services = new ServiceCollection();

        services.AddRunaxHttp().AddRestSharpTransport();

        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHttpTransportResolver>();

        resolver.Names.ShouldContain(HttpClientTransport.TransportName);
        resolver.Names.ShouldContain(RestSharpTransport.TransportName);
    }
}
