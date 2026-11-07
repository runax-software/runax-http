using Runax.Http.Pipeline;
using Runax.Http.Requests;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

public class RequestContextTests
{
    private static RequestContext Create(TransportRequest? request = null)
        => new(
            new ClientRequest { Method = RequestMethod.Get, Template = "/things" },
            request ?? new TransportRequest
            {
                Method = RequestMethod.Get,
                RequestUri = new Uri("https://api.example.com/things"),
                Headers = [new KeyValuePair<string, string>("Accept", "application/json")]
            },
            new RecordingTransport("ctx-test"));

    [Fact]
    public void Headers_are_copied_rather_than_shared_with_the_request()
    {
        // The retry loop holds the original request across attempts. A stage mutating it in place
        // would leak one attempt's headers into the next.
        var request = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("https://api.example.com/things"),
            Headers = [new KeyValuePair<string, string>("Accept", "application/json")]
        };

        var context = Create(request);
        context.SetHeader("X-Added", "1");

        request.Headers.Count.ShouldBe(1);
        context.Headers.Count.ShouldBe(2);
    }

    [Fact]
    public void SetHeader_replaces_rather_than_appends()
    {
        // What a stage re-running on a retry needs: appending would leave the stale value behind.
        var context = Create();

        context.SetHeader("X-Token", "first");
        context.SetHeader("X-Token", "second");

        context.Headers.Count(header => header.Key == "X-Token").ShouldBe(1);
        context.GetHeader("X-Token").ShouldBe("second");
    }

    [Fact]
    public void AddHeader_keeps_both_values()
    {
        var context = Create();

        context.AddHeader("X-Multi", "a");
        context.AddHeader("X-Multi", "b");

        context.Headers.Count(header => header.Key == "X-Multi").ShouldBe(2);
    }

    [Fact]
    public void Header_lookups_are_case_insensitive()
    {
        var context = Create();

        context.GetHeader("accept").ShouldBe("application/json");
        context.RemoveHeader("ACCEPT").ShouldBeTrue();
        context.GetHeader("Accept").ShouldBeNull();
    }

    [Fact]
    public void RemoveHeader_clears_every_value_under_the_name()
    {
        var context = Create();

        context.AddHeader("X-Multi", "a");
        context.AddHeader("X-Multi", "b");

        context.RemoveHeader("X-Multi").ShouldBeTrue();
        context.Headers.ShouldNotContain(header => header.Key == "X-Multi");
    }

    [Fact]
    public void RemoveHeader_reports_when_there_was_nothing_to_remove()
        => Create().RemoveHeader("X-Absent").ShouldBeFalse();

    [Fact]
    public void Items_is_allocated_only_when_used()
    {
        // Most calls never touch it, and a dictionary per request would be an allocation that
        // bought nothing.
        var context = Create();

        context.Items["key"] = "value";
        context.Items["key"].ShouldBe("value");
    }

    [Fact]
    public void The_attempt_starts_at_one()
        => Create().Attempt.ShouldBe(1);

    [Fact]
    public void The_rebuilt_request_carries_every_property()
    {
        // ToTransportRequest copies TransportRequest property by property, so a property added to
        // TransportRequest without being copied there would be silently dropped by the pipeline.
        // This counts them so that happens as a failing test rather than a lost proxy setting.
        using var stream = new MemoryStream([1, 2, 3]);

        var request = new TransportRequest
        {
            Method = RequestMethod.Put,
            RequestUri = new Uri("https://api.example.com/upload?x=1"),
            Headers = [new KeyValuePair<string, string>("Accept", "application/json")],
            Content = RequestContent.FromStream(stream, "application/octet-stream"),
            Timeout = TimeSpan.FromSeconds(7),
            Proxy = new RequestProxy("proxy.internal", 8080, ProxyProtocol.Socks5),
            Version = new Version(2, 0)
        };

        var context = Create(request);
        context.SetHeader("X-Added", "1");

        var rebuilt = context.ToTransportRequest();

        rebuilt.Method.ShouldBe(request.Method);
        rebuilt.RequestUri.ShouldBe(request.RequestUri);
        rebuilt.Content.ShouldBeSameAs(request.Content);
        rebuilt.Timeout.ShouldBe(request.Timeout);
        rebuilt.Proxy.ShouldBeSameAs(request.Proxy);
        rebuilt.Version.ShouldBe(request.Version);
        rebuilt.Headers.ShouldContain(header => header.Key == "X-Added");

        // If this number changes, ToTransportRequest needs the new property too.
        typeof(TransportRequest)
            .GetProperties()
            .Count(property => property.CanWrite)
            .ShouldBe(7, "a settable property was added to TransportRequest — copy it in RequestContext.ToTransportRequest()");
    }

    [Fact]
    public void A_rebuilt_request_does_not_change_underneath_a_transport()
    {
        // TransportRequest is documented as immutable and a transport may keep one. Handing over
        // the live header list meant a retry's SetHeader rewrote the headers of a request that had
        // already gone out — which showed up as the first attempt appearing to carry the second
        // attempt's token.
        var context = Create();

        context.SetHeader("X-Token", "first");
        var sent = context.ToTransportRequest();

        context.SetHeader("X-Token", "second");

        sent.Headers.ShouldContain(header => header.Key == "X-Token" && header.Value == "first");
        context.ToTransportRequest().Headers
            .ShouldContain(header => header.Key == "X-Token" && header.Value == "second");
    }

    [Fact]
    public void The_rebuilt_request_still_derives_its_required_capabilities()
    {
        // The capability check runs on the request as first built, so the rebuild has to preserve
        // whatever drives it — otherwise a streamed body could reach a transport that cannot stream.
        using var stream = new MemoryStream([1, 2, 3]);

        var context = Create(new TransportRequest
        {
            Method = RequestMethod.Put,
            RequestUri = new Uri("https://api.example.com/upload"),
            Content = RequestContent.FromStream(stream)
        });

        context.ToTransportRequest()
            .RequiredCapabilities.ShouldBe(TransportCapabilities.RequestStreaming);
    }
}
