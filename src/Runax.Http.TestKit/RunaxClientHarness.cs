using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Serialization;
using Runax.Http.Transports;

namespace Runax.Http.Testing;

/// <summary>
/// A generated client wired to a <see cref="TestTransport"/>, ready to assert on.
/// </summary>
/// <typeparam name="TContract">The client interface under test.</typeparam>
/// <remarks>
/// <para>
/// Stands up a real container with the real generated client and the real runtime, over a transport
/// that records instead of sending. That matters: a mocked client interface tests nothing about
/// your attributes, and mocking the executor tests nothing about the generated code. The things
/// worth asserting — that the path template resolved, that a collection became repeated query
/// keys, that the body serialized the way you expect — only happen when the whole stack is real
/// except the socket.
/// </para>
/// <code>
/// using var harness = RunaxClientHarness.For&lt;IRepositoryClient&gt;();
/// harness.Transport.RespondsJson("""{"name":"runax-http","stars":1}""");
///
/// var repository = await harness.Client.GetAsync("runax-software", "runax-http");
///
/// repository.Name.ShouldBe("runax-http");
/// harness.Last.Path.ShouldBe("/repos/runax-software/runax-http");
/// harness.Last.Header("Accept").ShouldBe("application/json");
/// </code>
/// </remarks>
public sealed class RunaxClientHarness<TContract> : IDisposable
    where TContract : class
{
    private readonly ServiceProvider _provider;

    internal RunaxClientHarness(ServiceProvider provider, TContract client, TestTransport transport)
    {
        _provider = provider;
        Client = client;
        Transport = transport;
    }

    /// <summary>The client under test.</summary>
    public TContract Client { get; }

    /// <summary>The transport, for scripting responses and inspecting requests.</summary>
    public TestTransport Transport { get; }

    /// <summary>The container, for resolving anything else the test needs.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Every request the client sent, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => Transport.Requests;

    /// <summary>The most recent request.</summary>
    public RecordedRequest Last => Transport.Last;

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Creates a <see cref="RunaxClientHarness{TContract}"/>.
/// </summary>
/// <remarks>
/// Separate from the generic type so the factories are not static members on it — and
/// <c>RunaxClientHarness.For&lt;IRepositoryClient&gt;()</c> reads better than the alternative.
/// </remarks>
public static class RunaxClientHarness
{
    /// <summary>Creates a harness.</summary>
    /// <param name="configure">Applied to the client's registration, as in production code.</param>
    /// <param name="transport">
    /// The transport to record on. A new one claiming every capability by default.
    /// </param>
    /// <param name="serializer">
    /// The serializer. Defaults to reflection-based <c>System.Text.Json</c> with web conventions,
    /// because a test should not need a source-generated context to assert on a request body.
    /// </param>
    /// <returns>The harness.</returns>
    /// <remarks>
    /// A base address is set to <c>https://runax.test</c> when the interface declares none and the
    /// caller does not either, so a contract written without <c>[BaseAddress]</c> can still be
    /// tested without every test supplying one.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Test-support package. The reflection-based serializer is the point: test "
                        + "assemblies are not trimmed, and requiring a source-generated context "
                        + "just to assert on a request body would be friction for no benefit.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Test-support package; test assemblies are not AOT-published.")]
    public static RunaxClientHarness<TContract> For<TContract>(
        Action<IRunaxClientBuilder<TContract>>? configure = null,
        TestTransport? transport = null,
        IContentSerializer? serializer = null)
        where TContract : class
    {
        var testTransport = transport ?? new TestTransport();
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();

        shared.UseSerializer(serializer ?? SystemTextJsonContentSerializer.CreateReflectionBased());
        shared.AddTransport(testTransport);
        shared.UseDefaultTransport(testTransport.Name);

        // Registered unconditionally: putting it inside a null-conditional argument would mean the
        // client is never registered when configure is null, silently.
        var client = services.AddRunaxClient<TContract>();

        client.Configure(options =>
            options.BaseAddress ??= new Uri("https://runax.test"));

        configure?.Invoke(client);

        var provider = services.BuildServiceProvider();

        return new RunaxClientHarness<TContract>(
            provider,
            provider.GetRequiredService<TContract>(),
            testTransport);
    }

    /// <summary>Creates a harness whose transport answers every request the same way.</summary>
    /// <param name="response">The response to every request.</param>
    /// <param name="configure">Applied to the client's registration.</param>
    /// <returns>The harness.</returns>
    public static RunaxClientHarness<TContract> Responding<TContract>(
        TestResponse response,
        Action<IRunaxClientBuilder<TContract>>? configure = null)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(response);

        var transport = new TestTransport();
        transport.AlwaysResponds(response);

        return For(configure, transport);
    }
}
