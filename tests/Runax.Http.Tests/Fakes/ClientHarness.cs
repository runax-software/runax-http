using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Transports;

namespace Runax.Http.Tests.Fakes;

/// <summary>
/// Stands up a real container with a real generated client, over a transport that records instead
/// of sending.
/// </summary>
/// <remarks>
/// Deliberately not a mock of the executor. The things most worth testing here — that the base
/// address is appended rather than replaced, that a collection becomes repeated query keys, that
/// transport precedence resolves the way it is documented — only happen when the generated code,
/// the registration and the runtime are wired together for real.
/// </remarks>
internal sealed class ClientHarness
{
    private ClientHarness(IRepositoryClient client, RecordingTransport transport, IServiceProvider services)
    {
        Client = client;
        Transport = transport;
        Services = services;
    }

    internal IRepositoryClient Client { get; }

    internal RecordingTransport Transport { get; }

    internal IServiceProvider Services { get; }

    /// <summary>The request the client produced, which is what most assertions are about.</summary>
    internal TransportRequest Request => Transport.Last;

    internal static ClientHarness Create(
        Func<TransportRequest, TransportResponse>? respond = null,
        TransportCapabilities capabilities = TransportCapabilities.None,
        Action<IRunaxClientBuilder<IRepositoryClient>>? configure = null,
        Action<IRunaxHttpBuilder>? configureShared = null)
    {
        var transport = new RecordingTransport("recording", capabilities, respond);
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        configureShared?.Invoke(shared);

        var client = services.AddRunaxClient<IRepositoryClient>();
        configure?.Invoke(client);

        var provider = services.BuildServiceProvider();

        return new ClientHarness(
            provider.GetRequiredService<IRepositoryClient>(),
            transport,
            provider);
    }

    /// <summary>The value of a header on the recorded request, or null if it was not sent.</summary>
    internal string? Header(string name)
    {
        foreach (var (key, value) in Request.Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }
}
