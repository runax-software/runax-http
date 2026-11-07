using Runax.Http.DependencyInjection;
using Runax.Http.Testing;

namespace Runax.Http;

/// <summary>Registers the test transport with a container you built yourself.</summary>
public static class TestTransportBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="TestTransport"/> and makes it the default.
    /// </summary>
    /// <param name="builder">The Runax.Http builder.</param>
    /// <param name="transport">The transport, or a new one.</param>
    /// <returns>The transport, so the test can script it and read it back.</returns>
    /// <remarks>
    /// For a test that stands up its own container — an integration test over a real
    /// <c>WebApplicationFactory</c>, say — and wants outbound HTTP recorded rather than sent.
    /// <see cref="RunaxClientHarness{TContract}"/> is the simpler route when the client is the
    /// thing under test.
    /// </remarks>
    public static TestTransport AddTestTransport(
        this IRunaxHttpBuilder builder,
        TestTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var testTransport = transport ?? new TestTransport();

        builder.AddTransport(testTransport);
        builder.UseDefaultTransport(testTransport.Name);

        // Returns the transport rather than the builder, breaking the fluent chain on purpose: the
        // transport is what the test needs a handle on, and making the caller fish it back out of
        // the container afterwards is the kind of friction that gets a test helper ignored.
        return testTransport;
    }
}
