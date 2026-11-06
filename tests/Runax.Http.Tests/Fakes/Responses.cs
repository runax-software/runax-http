using System.Text;
using Runax.Http.Transports;

namespace Runax.Http.Tests.Fakes;

/// <summary>Builds transport responses for tests to answer with.</summary>
internal static class Responses
{
    internal static TransportResponse Json(string body, int statusCode = 200)
        => new(statusCode)
        {
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")],
            Content = new MemoryStream(Encoding.UTF8.GetBytes(body))
        };

    internal static TransportResponse Empty(int statusCode = 204) => new(statusCode);

    internal static TransportResponse Error(int statusCode, string body = "")
        => new(statusCode)
        {
            ReasonPhrase = statusCode == 404 ? "Not Found" : null,
            Content = new MemoryStream(Encoding.UTF8.GetBytes(body))
        };
}
