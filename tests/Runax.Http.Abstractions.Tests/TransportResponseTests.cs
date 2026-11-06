using Runax.Http.Transports;

namespace Runax.Http.Abstractions.Tests;

/// <summary>
/// A response owns transport resources — the HTTP library's own response object, a live body
/// stream, a pooled connection — so getting disposal right is the difference between a working
/// client and one that leaks connections under load.
/// </summary>
public class TransportResponseTests
{
    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>A transport resource that only implements the async form.</summary>
    private sealed class AsyncOnlyResource : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A transport resource that implements both, as a real one normally does.</summary>
    private sealed class BothResource : IAsyncDisposable, IDisposable
    {
        public int SyncDisposeCount { get; private set; }

        public int AsyncDisposeCount { get; private set; }

        public void Dispose() => SyncDisposeCount++;

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(301, false)]
    [InlineData(404, false)]
    [InlineData(503, false)]
    public void Success_is_the_2xx_range(int statusCode, bool expected)
        => new TransportResponse(statusCode).IsSuccessStatusCode.ShouldBe(expected);

    [Fact]
    public void Disposing_disposes_the_body()
    {
        var body = new TrackingStream();
        var response = new TransportResponse(200) { Content = body };

        response.Dispose();

        body.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Disposing_asynchronously_disposes_the_body()
    {
        var body = new TrackingStream();
        var response = new TransportResponse(200) { Content = body };

        await response.DisposeAsync();

        body.Disposed.ShouldBeTrue();
    }

    [Fact]
    public void Disposing_releases_the_transport_resource()
    {
        var resource = new BothResource();

        new TransportResponse(200, resource).Dispose();

        resource.SyncDisposeCount.ShouldBe(1);
        resource.AsyncDisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Disposing_asynchronously_releases_the_transport_resource()
    {
        var resource = new BothResource();

        await new TransportResponse(200, resource).DisposeAsync();

        resource.AsyncDisposeCount.ShouldBe(1);
        resource.SyncDisposeCount.ShouldBe(0);
    }

    [Fact]
    public void An_async_only_resource_is_still_released_on_the_synchronous_path()
    {
        // Synchronous Dispose() is reachable from a using statement in a non-async caller, so an
        // async-only resource has to be drained rather than quietly leaked.
        var resource = new AsyncOnlyResource();

        new TransportResponse(200, resource).Dispose();

        resource.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var resource = new BothResource();
        var response = new TransportResponse(200, resource) { Content = new TrackingStream() };

        response.Dispose();
        response.Dispose();

        resource.SyncDisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Mixing_the_two_disposal_forms_is_harmless()
    {
        var resource = new BothResource();
        var response = new TransportResponse(200, resource);

        response.Dispose();
        await response.DisposeAsync();

        resource.SyncDisposeCount.ShouldBe(1);
        resource.AsyncDisposeCount.ShouldBe(0);
    }

    [Fact]
    public void A_response_without_a_body_or_a_resource_disposes_cleanly()
        => Should.NotThrow(() => new TransportResponse(204).Dispose());

    [Fact]
    public void Headers_default_to_empty_rather_than_null()
        => new TransportResponse(200).Headers.ShouldBeEmpty();
}
