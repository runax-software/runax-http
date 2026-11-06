using Runax.Http;
using Runax.Http.Transports;

// Exercises the shipped surface under native AOT. The publish is the real gate — it fails on any
// trim or AOT warning — but running the binary catches a warning-free build that still breaks at
// run time, which is the failure mode that actually reaches users.

var transport = new NullTransport();

var request = new TransportRequest
{
    Method = RequestMethod.Post,
    RequestUri = new Uri("https://api.example.com/repos/runax-software/runax-http/issues"),
    Headers = [new KeyValuePair<string, string>("Accept", "application/json")],
    Content = RequestContent.FromString("""{"title":"aot"}""", "application/json"),
    Timeout = TimeSpan.FromSeconds(10)
};

Check(request.RequiredCapabilities == TransportCapabilities.PerRequestTimeout,
    "required capabilities derived from the request");

Check(TransportCapabilityException.ForMissing(transport, request.RequiredCapabilities) is null,
    "capability check passes for a transport that has what the request needs");

var gap = TransportCapabilityException.ForMissing(transport, TransportCapabilities.Http3);
Check(gap is { Missing: TransportCapabilities.Http3 }, "capability gap is reported");

using var response = await transport.SendAsync(request);
Check(response.StatusCode == 201 && response.IsSuccessStatusCode, "response round trip");

var apiResponse = new ApiResponse<string>(response.StatusCode) { Content = "ok" };
Check(apiResponse.EnsureSuccess() == "ok", "ApiResponse<T>.EnsureSuccess");

var error = new HttpApiException("nope") { StatusCode = 404, Method = RequestMethod.Get };
Check(error.StatusCode == 404 && error.Method == RequestMethod.Get, "HttpApiException detail");

// Multipart and form bodies go through the structured path rather than being pre-encoded, so
// construct both to make sure nothing on that route needs reflection.
var multipart = RequestContent.FromMultipart(
    [new MultipartPart("file", RequestContent.FromBytes(new byte[] { 1, 2, 3 }), "data.bin")]);
Check(multipart.Parts.Count == 1, "multipart content");

var form = RequestContent.FromForm([new KeyValuePair<string, string?>("state", "open")]);
Check(form.ContentType == "application/x-www-form-urlencoded", "form content");

Console.WriteLine("AOT smoke test passed.");
return 0;

static void Check(bool condition, string what)
{
    if (condition)
        return;

    Console.Error.WriteLine($"AOT smoke test failed: {what}");
    Environment.Exit(1);
}

/// <summary>
/// Stands in for a real transport. The point is to run the abstractions through AOT, not to make
/// a network call, so this answers without one.
/// </summary>
internal sealed class NullTransport : IHttpTransport
{
    public string Name => "null";

    public TransportCapabilities Capabilities =>
        TransportCapabilities.PerRequestTimeout | TransportCapabilities.Multipart;

    public Task<TransportResponse> SendAsync(
        TransportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = new TransportResponse(201)
        {
            ReasonPhrase = "Created",
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")],
            Content = new MemoryStream("""{"number":1}"""u8.ToArray()),
            FinalUri = request.RequestUri
        };

        return Task.FromResult(response);
    }
}
