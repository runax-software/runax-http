using Runax.Http.Transports;
using Runax.Http.Transports.Conformance;

namespace Runax.Http.Transports.RestSharp.Tests;

/// <summary>
/// The shared transport suite, run against RestSharp.
/// </summary>
/// <remarks>
/// The whole file. That is the point of the suite: a new transport inherits the specification
/// rather than its author rediscovering it, and the first transport's accidental behaviour does
/// not become the de facto standard.
/// </remarks>
public sealed class RestSharpConformanceTests : TransportConformanceTests
{
    protected override IHttpTransport CreateTransport() => new RestSharpTransport();
}
