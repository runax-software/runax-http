using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Transports;

namespace Runax.Http.ProxyFallback.Tests;

/// <summary>
/// End-to-end over interfaces the generator never saw: registration, the proxy, the real runtime,
/// and a transport that records what it was handed.
/// </summary>
/// <remarks>
/// Request-for-request agreement with the generated client is proved in Runax.Http.Tests, where
/// both paths can run over the same interfaces. This suite covers what only exists without a
/// generated client: that registration falls back at all, and that a broken contract is refused
/// before a request is ever made.
/// </remarks>
public class ProxyFallbackTests
{
    [Fact]
    public void Without_the_fallback_a_contract_with_no_generated_client_is_still_refused()
    {
        var services = new ServiceCollection();

        var failure = Should.Throw<InvalidOperationException>(() => services.AddRunaxClient<IRepositoryClient>());

        failure.Message.ShouldContain("No generated implementation");
        failure.Message.ShouldContain("AddRunaxClientWithProxyFallback");
    }

    [Fact]
    public async Task The_fallback_implements_the_contract_with_a_proxy_over_the_real_runtime()
    {
        var (client, transport) = Create(_ => Json("""{"name":"runax-http","stars":7}"""));

        var repository = await client.GetAsync("runax-software", "runax-http");

        client.ShouldBeAssignableTo<DispatchProxy>();
        repository.ShouldBe(new Repository("runax-http", 7));

        transport.Last.Method.ShouldBe(RequestMethod.Get);
        transport.Last.RequestUri.ToString().ShouldBe("https://api.example.com/v3/repos/runax-software/runax-http");
        transport.Last.Headers.ShouldContain(new KeyValuePair<string, string>("User-Agent", "runax-proxy-tests"));
    }

    [Fact]
    public async Task Query_values_follow_the_generators_naming_and_omission_rules()
    {
        var (client, transport) = Create(_ => Json("[]"));

        await client.ListIssuesAsync("o", "n", labels: ["bug", "needs triage"]);

        // perPage is camelCased by default, the null state is omitted rather than sent empty, and
        // the collection becomes one repeated key per item.
        transport.Last.RequestUri.Query.ShouldBe("?perPage=30&labels=bug&labels=needs%20triage");
    }

    [Fact]
    public async Task A_body_a_header_and_a_cancellation_token_reach_the_transport()
    {
        var (client, transport) = Create(_ => Json("""{"number":1,"title":"t"}"""));
        var key = Guid.Parse("8f14e45f-ceea-467f-a2a3-1c2b3d4e5f60");
        using var cancellation = new CancellationTokenSource();

        var issue = await client.CreateIssueAsync("o", "n", new NewIssue("Broken"), key, cancellation.Token);

        issue.ShouldBe(new Issue(1, "t"));
        transport.Last.Method.ShouldBe(RequestMethod.Post);
        transport.Last.Headers.ShouldContain(new KeyValuePair<string, string>("Idempotency-Key", key.ToString()));
        Encoding.UTF8.GetString(((BinaryContent)transport.Last.Content!).Bytes.Span).ShouldBe("""{"title":"Broken"}""");
        transport.LastToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task A_declared_retry_is_carried_on_the_request()
    {
        var (client, transport) = Create(_ => new TransportResponse(503));

        await Should.ThrowAsync<HttpApiException>(
            () => client.CreateIssueAsync("o", "n", new NewIssue("t"), Guid.Empty));

        // [Retry(3, ...)] on a POST, with no retry policy on the client.
        transport.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Each_return_shape_behaves_as_it_does_on_a_generated_client()
    {
        var (client, _) = Create(request => request.RequestUri.AbsolutePath.EndsWith("/health", StringComparison.Ordinal)
            ? new TransportResponse(200)
            : new TransportResponse(404) { Content = new MemoryStream("gone"u8.ToArray()) });

        // Task<ApiResponse>, declared on an inherited interface.
        (await client.HealthAsync()).StatusCode.ShouldBe(200);

        // Task<ApiResponse<T>> reports an error status instead of throwing it.
        var missing = await client.TryGetAsync("o", "n");
        missing.StatusCode.ShouldBe(404);
        missing.Error!.ResponseContent.ShouldBe("gone");

        // Task throws it.
        (await Should.ThrowAsync<HttpApiException>(() => client.DeleteAsync("o", "n"))).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task Declared_responses_are_matched_in_order()
    {
        var (client, _) = Create(_ => Json("""{"error":"rate limited"}"""));

        var result = await client.GetLegacyAsync("o", "n");

        result.ValueType.ShouldBe(typeof(Problem));
        result.Value.ShouldBe(new Problem("rate limited"));
    }

    [Fact]
    public async Task A_capability_the_transport_lacks_is_refused_before_sending()
    {
        var (client, transport) = Create(_ => new TransportResponse(200));

        await Should.ThrowAsync<TransportCapabilityException>(
            () => client.UploadAsync([new MultipartPart("file", RequestContent.FromString("hi"), "a.txt")]));

        transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void A_declared_transport_is_validated_at_startup_like_a_generated_one()
    {
        var services = new ServiceCollection();
        services.AddRunaxHttp().UseSystemTextJson().AddTransport(new RecordingTransport());
        services.AddRunaxClientWithProxyFallback<IMisspelledTransportClient>();

        var failure = Should.Throw<UnknownTransportException>(
            () => services.BuildServiceProvider().GetRequiredService<IMisspelledTransportClient>());

        failure.RequestedName.ShouldBe("restsharpp");
    }

    [Fact]
    public void A_contract_that_cannot_be_implemented_is_refused_at_registration_with_every_problem_listed()
    {
        var services = new ServiceCollection();

        var failure = Should.Throw<InvalidOperationException>(
            () => services.AddRunaxClientWithProxyFallback<IBrokenClient>());

        failure.Message.ShouldContain("cannot be implemented by a runtime proxy");

        foreach (var id in new[]
                 {
                     "RXH002", "RXH003", "RXH005", "RXH009", "RXH011", "RXH013", "RXH015", "RXH019", "RXH021", "RXH023"
                 })
        {
            failure.Message.ShouldContain(id);
        }
    }

    private static (IRepositoryClient Client, RecordingTransport Transport) Create(
        Func<TransportRequest, TransportResponse> respond)
    {
        var transport = new RecordingTransport(respond);
        var services = new ServiceCollection();

        services.AddRunaxHttp().UseSystemTextJson().AddTransport(transport).UseDefaultTransport(transport.Name);
        services.AddRunaxClientWithProxyFallback<IRepositoryClient>();

        return (services.BuildServiceProvider().GetRequiredService<IRepositoryClient>(), transport);
    }

    private static TransportResponse Json(string body)
        => new(200)
        {
            Headers = [new KeyValuePair<string, string>("Content-Type", "application/json")],
            Content = new MemoryStream(Encoding.UTF8.GetBytes(body))
        };
}

/// <summary>A transport that records what it was asked to send and answers from a script.</summary>
internal sealed class RecordingTransport(Func<TransportRequest, TransportResponse>? respond = null) : IHttpTransport
{
    public string Name => "recording";

    public TransportCapabilities Capabilities => TransportCapabilities.None;

    internal List<TransportRequest> Requests { get; } = [];

    internal TransportRequest Last => Requests[^1];

    internal CancellationToken LastToken { get; private set; }

    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        LastToken = cancellationToken;

        return Task.FromResult(respond?.Invoke(request) ?? new TransportResponse(200));
    }
}
