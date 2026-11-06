using Runax.Http;
using Runax.Http.Transports;

namespace Runax.Http.Abstractions.Tests;

public class TransportCapabilityExceptionTests
{
    private sealed class FakeTransport(TransportCapabilities capabilities) : IHttpTransport
    {
        public string Name => "fake";

        public TransportCapabilities Capabilities { get; } = capabilities;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by these tests.");
    }

    [Fact]
    public void ForMissing_returns_null_when_the_transport_can_do_everything_required()
    {
        var transport = new FakeTransport(
            TransportCapabilities.RequestStreaming | TransportCapabilities.Multipart);

        TransportCapabilityException
            .ForMissing(transport, TransportCapabilities.RequestStreaming)
            .ShouldBeNull();
    }

    [Fact]
    public void ForMissing_returns_null_when_nothing_is_required()
        => TransportCapabilityException
            .ForMissing(new FakeTransport(TransportCapabilities.None), TransportCapabilities.None)
            .ShouldBeNull();

    [Fact]
    public void ForMissing_reports_only_the_capabilities_that_are_actually_missing()
    {
        var transport = new FakeTransport(TransportCapabilities.Multipart);

        var exception = TransportCapabilityException.ForMissing(
            transport,
            TransportCapabilities.Multipart | TransportCapabilities.RequestStreaming,
            new Uri("https://api.example.com/upload"));

        exception.ShouldNotBeNull();
        exception.Missing.ShouldBe(TransportCapabilities.RequestStreaming);
        exception.TransportName.ShouldBe("fake");
        exception.RequestUri.ShouldBe(new Uri("https://api.example.com/upload"));
    }

    [Fact]
    public void The_message_names_the_transport_and_what_it_cannot_do()
    {
        var exception = TransportCapabilityException.ForMissing(
            new FakeTransport(TransportCapabilities.None),
            TransportCapabilities.PerRequestProxy);

        exception.ShouldNotBeNull();
        exception.Message.ShouldContain("fake");
        exception.Message.ShouldContain(nameof(TransportCapabilities.PerRequestProxy));
    }

    [Fact]
    public void A_capability_gap_is_catchable_as_a_transport_failure()
    {
        // Picking the wrong transport and failing to reach the host are both "the request never
        // happened", so a caller that only cares about that distinction can catch one type.
        var exception = TransportCapabilityException.ForMissing(
            new FakeTransport(TransportCapabilities.None),
            TransportCapabilities.Http3);

        exception.ShouldBeAssignableTo<TransportException>();
    }
}
