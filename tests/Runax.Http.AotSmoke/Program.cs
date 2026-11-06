using Runax.Http;
using Runax.Http.AotSmoke;
using Runax.Http.DependencyInjection;
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

// The real test: a generated client, resolved from a container, serializing through a
// source-generated JSON context, over a transport — all compiled ahead of time.
var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();

var shared = services.AddRunaxHttp();
shared.UseSystemTextJson(AotJson.Options);
shared.AddTransport(new EchoTransport());
shared.UseDefaultTransport("echo");

services.AddRunaxClient<IThingClient>().WithBaseAddress("https://api.example.com/v1");

var provider = global::Microsoft.Extensions.DependencyInjection
    .ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

var things = global::Microsoft.Extensions.DependencyInjection
    .ServiceProviderServiceExtensions.GetRequiredService<IThingClient>(provider);

var fetched = await things.GetAsync(7);
Check(fetched is { Id: 7, Name: "echo" }, "generated client deserialized a response");

var created = await things.CreateAsync(new NewThing("made"));
Check(created.Name == "echo", "generated client serialized a body and read the reply");

var probed = await things.TryGetAsync("missing");
Check(probed.IsSuccessStatusCode, "generated client returned ApiResponse<T>");

await things.DeleteAsync(7);

using (TransportScope.Use("echo"))
    Check((await things.GetAsync(1)).Id == 7, "transport scope resolved");

Console.WriteLine("AOT smoke test passed.");
return 0;

static void Check(bool condition, string what)
{
    if (condition)
        return;

    Console.Error.WriteLine($"AOT smoke test failed: {what}");
    Environment.Exit(1);
}
