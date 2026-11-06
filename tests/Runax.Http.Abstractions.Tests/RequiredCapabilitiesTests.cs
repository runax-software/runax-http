using Runax.Http;
using Runax.Http.Transports;

namespace Runax.Http.Abstractions.Tests;

/// <summary>
/// <see cref="TransportRequest.RequiredCapabilities"/> is what makes the capability check
/// mechanical instead of something every transport has to remember to do, so what it derives
/// from a request — and what it deliberately doesn't — is worth pinning down.
/// </summary>
public class RequiredCapabilitiesTests
{
    private static TransportRequest Request(RequestContent? content = null) => new()
    {
        Method = RequestMethod.Get,
        RequestUri = new Uri("https://api.example.com/things"),
        Content = content
    };

    [Fact]
    public void A_plain_request_requires_nothing_beyond_the_baseline()
        => Request().RequiredCapabilities.ShouldBe(TransportCapabilities.None);

    [Fact]
    public void A_buffered_body_requires_nothing_beyond_the_baseline()
        => Request(RequestContent.FromString("{}", "application/json"))
            .RequiredCapabilities.ShouldBe(TransportCapabilities.None);

    [Fact]
    public void A_streamed_body_requires_request_streaming()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        Request(RequestContent.FromStream(stream))
            .RequiredCapabilities.ShouldBe(TransportCapabilities.RequestStreaming);
    }

    [Fact]
    public void A_multipart_body_requires_multipart()
    {
        var content = RequestContent.FromMultipart(
            [new MultipartPart("file", RequestContent.FromString("hello"), "hello.txt")]);

        Request(content).RequiredCapabilities.ShouldBe(TransportCapabilities.Multipart);
    }

    [Fact]
    public void A_form_body_requires_nothing_because_every_transport_can_encode_one()
        => Request(RequestContent.FromForm([new KeyValuePair<string, string?>("a", "b")]))
            .RequiredCapabilities.ShouldBe(TransportCapabilities.None);

    [Fact]
    public void A_per_request_proxy_requires_per_request_proxy()
    {
        var request = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("https://api.example.com/things"),
            Proxy = new RequestProxy("proxy.internal", 8080)
        };

        request.RequiredCapabilities.ShouldBe(TransportCapabilities.PerRequestProxy);
    }

    [Fact]
    public void A_per_request_timeout_requires_per_request_timeout()
    {
        var request = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("https://api.example.com/things"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        request.RequiredCapabilities.ShouldBe(TransportCapabilities.PerRequestTimeout);
    }

    [Theory]
    [InlineData(2, 0, TransportCapabilities.Http2)]
    [InlineData(3, 0, TransportCapabilities.Http3)]
    public void An_explicit_http_version_requires_that_version(int major, int minor, TransportCapabilities expected)
    {
        var request = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("https://api.example.com/things"),
            Version = new Version(major, minor)
        };

        request.RequiredCapabilities.ShouldBe(expected);
    }

    [Fact]
    public void Http_1_1_requires_nothing_because_every_transport_speaks_it()
    {
        var request = new TransportRequest
        {
            Method = RequestMethod.Get,
            RequestUri = new Uri("https://api.example.com/things"),
            Version = new Version(1, 1)
        };

        request.RequiredCapabilities.ShouldBe(TransportCapabilities.None);
    }

    [Fact]
    public void Requirements_accumulate()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var request = new TransportRequest
        {
            Method = RequestMethod.Post,
            RequestUri = new Uri("https://api.example.com/things"),
            Content = RequestContent.FromStream(stream),
            Proxy = new RequestProxy("proxy.internal", 8080, ProxyProtocol.Socks5),
            Timeout = TimeSpan.FromSeconds(30),
            Version = new Version(2, 0)
        };

        request.RequiredCapabilities.ShouldBe(
            TransportCapabilities.RequestStreaming
            | TransportCapabilities.PerRequestProxy
            | TransportCapabilities.PerRequestTimeout
            | TransportCapabilities.Http2);
    }

    [Fact]
    public void Response_side_capabilities_are_not_derived_from_the_request()
    {
        // ResponseStreaming, Cookies, AutomaticDecompression and AutomaticRedirect are
        // properties of how a transport is configured, not of a single request, so they are
        // checked where that configuration is applied rather than here. If this ever starts
        // failing, the capability model has been extended without deciding where it's enforced.
        using var stream = new MemoryStream([1, 2, 3]);

        var everything = Request(RequestContent.FromStream(stream)).RequiredCapabilities;

        everything.HasFlag(TransportCapabilities.ResponseStreaming).ShouldBeFalse();
        everything.HasFlag(TransportCapabilities.Cookies).ShouldBeFalse();
        everything.HasFlag(TransportCapabilities.AutomaticDecompression).ShouldBeFalse();
        everything.HasFlag(TransportCapabilities.AutomaticRedirect).ShouldBeFalse();
    }
}
