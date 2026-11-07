using Runax.Http.Transports;
using Runax.Http.Transports.Conformance;

namespace Runax.Http.Tests;

/// <summary>The shared transport suite, run against the built-in transport.</summary>
public sealed class HttpClientConformanceTests : TransportConformanceTests
{
    protected override IHttpTransport CreateTransport()
        => new HttpClientTransport(new HttpClient());
}
