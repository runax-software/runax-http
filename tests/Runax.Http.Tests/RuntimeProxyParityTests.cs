using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Proxy;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// The runtime proxy against the generated client, on the same interfaces: every call is made
/// through both, and what reaches the transport has to be identical.
/// </summary>
/// <remarks>
/// The proxy re-implements the generator's binding rules by reflection, and the risk of that is
/// drift — a key named one way here and another way there, from one interface. The contracts in
/// this project already exercise one of everything the generator supports, so running them
/// through both paths is the specification, rather than a second set of expectations that could
/// drift on its own.
/// </remarks>
public class RuntimeProxyParityTests
{
    private static readonly Guid TraceId = Guid.Parse("8f14e45f-ceea-467f-a2a3-1c2b3d4e5f60");

    private static readonly SearchFilter Filter = new()
    {
        State = "open",
        SortBy = "created",
        Labels = ["bug", "needs triage"],
        Since = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        Archived = false,
        Order = SearchOrder.Descending,
        Page = 2
    };

    private static readonly ReportFilter Report = new()
    {
        State = "open",
        PerPage = 50,
        HTTPReferer = "https://example.com",
        Top10Items = 3,
        Exact = "x"
    };

    [Fact]
    public async Task A_proxied_client_is_a_dispatch_proxy_and_answers_like_the_generated_one()
    {
        var (generated, proxied) = await Both<IRepositoryClient, Repository>(
            client => client.GetAsync("runax-software", "runax-http"),
            _ => Responses.Json("""{"name":"runax-http","stars":7}"""));

        proxied.ShouldBe(generated);
        proxied.ShouldBe(new Repository("runax-http", 7));
    }

    [Fact]
    public async Task Repository_client_requests_match()
    {
        await Same<IRepositoryClient>(client => client.GetAsync("owner/with/slashes", "name?with=query"));
        await Same<IRepositoryClient>(client => client.ListIssuesAsync("o", "n"), _ => Responses.Json("[]"));
        await Same<IRepositoryClient>(
            client => client.ListIssuesAsync("o", "n", "closed", 50, ["bug", "x y"]), _ => Responses.Json("[]"));
        await Same<IRepositoryClient>(client => client.CreateIssueAsync("o", "n", new NewIssue("Title", "Body")));
        await Same<IRepositoryClient>(client => client.DeleteAsync("o", "n"), _ => Responses.Empty());
        await Same<IRepositoryClient>(client => client.TryGetAsync("o", "n"), _ => Responses.Error(404, "gone"));
        await Same<IRepositoryClient>(client => client.ExistsAsync("o", "n"), _ => Responses.Empty(200));
        await Same<IRepositoryClient>(client => client.GetWithHeaderAsync("Bearer token", TraceId));
        await Same<IRepositoryClient>(client => client.PutReadmeAsync("o", "n", "# Title"), _ => Responses.Empty());
    }

    [Fact]
    public async Task Query_object_and_dictionary_requests_match()
    {
        await Same<ISearchClient>(client => client.SearchAsync(Filter));
        await Same<ISearchClient>(client => client.SearchNullableAsync(null));
        await Same<ISearchClient>(client => client.SearchNullableAsync(Filter));
        await Same<ISearchClient>(client => client.SearchPointAsync(new PointFilter(1, 2)));
        await Same<ISearchClient>(client => client.SearchNullablePointAsync(null));
        await Same<ISearchClient>(client => client.SearchNullablePointAsync(new PointFilter(3, 4)));
        await Same<ISearchClient>(client => client.SearchDynamicAsync(new() { ["a"] = "1", ["b"] = null }));
        await Same<ISearchClient>(client => client.SearchDynamicTypedAsync([new("n", 5), new("when", TraceId)]));
        await Same<ISearchClient>(client => client.SearchMixedAsync("text", Filter));
    }

    [Fact]
    public async Task Query_naming_policies_match()
    {
        await Same<ISnakeCaseClient>(client => client.ListAsync(Report, 3));
        await Same<ISnakeCaseClient>(client => client.LegacyAsync(Report));
        await Same<IDefaultNamingClient>(client => client.ListAsync(Report));
    }

    [Fact]
    public async Task Form_and_multipart_bodies_match()
    {
        await Same<IFormClient>(client => client.SubmitAsync(new() { ["a"] = "1", ["b"] = "two words" }));
        await Same<IFormClient>(client => client.SubmitTypedAsync([new("n", 1), new("none", null), new("many", new[] { 1, 2 })]));
        await Same<IFormClient>(
            client => client.UploadAsync([new MultipartPart("file", RequestContent.FromString("hi"), "a.txt")]),
            capabilities: TransportCapabilities.Multipart);
    }

    [Fact]
    public async Task Declared_responses_are_matched_the_same_way()
    {
        await SameResult(client => client.GetAsync(1), Responses.Json("""{"id":1,"name":"w"}"""));
        await SameResult(client => client.GetAsync(1), Responses.Json("""{"message":"no"}""", 404));
        await SameResult(client => client.GetAsync(1), Responses.Json("""{"errors":["bad"]}""", 422));
        await SameResult(client => client.GetAsync(1), Responses.Json("{}", 500));
        await SameResult(client => client.GetHistoryAsync(1), Responses.Json("""{"error":"x","code":2}""", 503));
        await SameResult(client => client.GetLegacyAsync(1), Responses.Json("""{"error":"x","code":2}"""));
        await SameResult(client => client.GetLegacyAsync(1), Responses.Json("""{"id":1,"name":"w"}"""));
        await SameResult(client => client.GetEnvelopeAsync(1), Responses.Json("""{"success":false,"error":"x","code":1}"""));
        await SameResult(client => client.GetEnvelopeAsync(1), Responses.Json("""{"success":true,"data":{"id":1,"name":"w"}}"""));
        await SameResult(client => client.GetCombinedAsync(1), Responses.Json("""{"error":"x","code":2}"""));
        await SameResult(client => client.GetCombinedAsync(1), Responses.Json("""{"error":"x","code":2}""", 201));
        await SameResult(client => client.GetStrictAsync(1), Responses.Json("""{"unexpected":true}""", 418));

        var flagged = Responses.Json("""{"error":"x","code":2}""");
        await SameResult(client => client.GetByHeaderAsync(1), WithHeader(flagged, "X-Error", "1"));
        await SameResult(client => client.GetByHeaderAsync(1), Responses.Json("""{"id":1,"name":"w"}"""));
    }

    [Fact]
    public async Task Per_method_retry_declarations_are_honoured_the_same_way()
    {
        // A retry override is invisible on any one request, so it is compared by what it causes:
        // how many attempts a persistently unavailable server sees.
        foreach (var call in new Func<IFlakyClient, Task>[]
                 {
                     client => client.GetReportAsync(1),
                     client => client.GetSlowReportAsync(1),
                     client => client.GetOnceAsync(1),
                     client => client.CreateReportAsync(new NewIssue("t")),
                     client => client.ListReportsAsync()
                 })
        {
            var generated = await Record(Generated<IFlakyClient>, call, _ => Responses.Error(503), retryAll: true);
            var proxied = await Record(Proxied<IFlakyClient>, call, _ => Responses.Error(503), retryAll: true);

            proxied.Count.ShouldBe(generated.Count);
            proxied.ShouldBe(generated);
        }
    }

    [Fact]
    public async Task A_declared_transport_is_chosen_the_same_way()
    {
        var generated = await Both<ITrafficClient, Repository>(
            client => client.GetTrafficAsync("o", "n"),
            _ => Responses.Json("""{"name":"n","stars":0}"""),
            extraTransport: "declared");

        generated.Generated.ShouldBe(generated.Proxied);
    }

    [Fact]
    public void The_proxy_describes_a_contract_as_the_generator_does()
    {
        foreach (var contract in new[]
                 {
                     typeof(IRepositoryClient), typeof(ISearchClient), typeof(IQuirkyClient), typeof(IFormClient),
                     typeof(IFlakyClient), typeof(ITrafficClient), typeof(ISnakeCaseClient)
                 })
        {
            GeneratedClientRegistry.TryGet(contract, out var generated).ShouldBeTrue();

            var proxied = ProxyContract.For(contract).Description;

            proxied.Contract.ShouldBe(contract);
            proxied.BaseAddress.ShouldBe(generated!.BaseAddress);
            proxied.TransportName.ShouldBe(generated.TransportName);
            proxied.DefaultHeaders.ShouldBe(generated.DefaultHeaders);
            proxied.DeclaredTransportNames.ShouldBe(generated.DeclaredTransportNames);
        }
    }

    [Fact]
    public void Fallback_registration_prefers_the_generated_implementation()
    {
        var services = new ServiceCollection();
        services.AddRunaxHttp().UseSystemTextJson().AddTransport(new RecordingTransport());
        services.AddRunaxClientWithProxyFallback<IRepositoryClient>();

        var client = services.BuildServiceProvider().GetRequiredService<IRepositoryClient>();

        client.ShouldNotBeAssignableTo<DispatchProxy>();
    }

    private static async Task Same<TContract>(
        Func<TContract, Task> call,
        Func<TransportRequest, TransportResponse>? respond = null,
        TransportCapabilities capabilities = TransportCapabilities.None)
        where TContract : class
    {
        respond ??= _ => Responses.Json("""{"id":1,"name":"w","stars":1}""");

        var generated = await Record(Generated<TContract>, call, respond, capabilities: capabilities);
        var proxied = await Record(Proxied<TContract>, call, respond, capabilities: capabilities);

        proxied.ShouldBe(generated);
    }

    private static async Task SameResult(Func<IQuirkyClient, Task<ApiResult>> call, TransportResponse response)
    {
        var body = await new StreamReader(response.Content!).ReadToEndAsync();

        TransportResponse Respond(TransportRequest _) => new(response.StatusCode)
        {
            Headers = response.Headers,
            Content = new MemoryStream(Encoding.UTF8.GetBytes(body))
        };

        var (generated, proxied) = await Both<IQuirkyClient, ApiResult>(call, Respond);

        proxied.StatusCode.ShouldBe(generated.StatusCode);
        proxied.ValueType.ShouldBe(generated.ValueType);
        // Compared as JSON: a record holding an array compares that array by reference.
        JsonSerializer.Serialize(proxied.Value).ShouldBe(JsonSerializer.Serialize(generated.Value));
        proxied.UnmatchedContent.ShouldBe(generated.UnmatchedContent);
    }

    private static async Task<(TResult Generated, TResult Proxied)> Both<TContract, TResult>(
        Func<TContract, Task<TResult>> call,
        Func<TransportRequest, TransportResponse> respond,
        string? extraTransport = null)
        where TContract : class
    {
        TResult? generated = default, proxied = default;

        var generatedRequests = await Record<TContract>(
            Generated<TContract>, async client => generated = await call(client), respond, extraTransport: extraTransport);

        var proxiedRequests = await Record<TContract>(
            Proxied<TContract>,
            async client =>
            {
                client.ShouldBeAssignableTo<DispatchProxy>();
                proxied = await call(client);
            },
            respond,
            extraTransport: extraTransport);

        proxiedRequests.ShouldBe(generatedRequests);

        return (generated!, proxied!);
    }

    private static void Generated<TContract>(IServiceCollection services)
        where TContract : class
        => services.AddRunaxClient<TContract>();

    private static void Proxied<TContract>(IServiceCollection services)
        where TContract : class
        => RunaxHttpServiceCollectionExtensions.Register<TContract>(
            services, ProxyContract.For(typeof(TContract)).Description);

    /// <summary>Makes one call through a fresh container and describes every request it sent.</summary>
    private static async Task<List<string>> Record<TContract>(
        Action<IServiceCollection> register,
        Func<TContract, Task> call,
        Func<TransportRequest, TransportResponse> respond,
        bool retryAll = false,
        TransportCapabilities capabilities = TransportCapabilities.None,
        string? extraTransport = null)
        where TContract : class
    {
        var transport = new RecordingTransport("recording", capabilities, respond);
        var extra = extraTransport is null ? null : new RecordingTransport(extraTransport, capabilities, respond);
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp().UseSystemTextJson().AddTransport(transport);

        if (extra is not null)
            shared.AddTransport(extra);

        shared.UseDefaultTransport(transport.Name);

        register(services);

        var provider = services.BuildServiceProvider();

        if (retryAll)
        {
            // Two attempts for any method that declares nothing, with no waiting, so a declaration
            // that raises, lowers or leaves the count alone each shows up as a different total.
            var retry = provider.GetRequiredService<RunaxClientRegistration<TContract>>().Options.Retry;
            retry.MaxAttempts = 2;
            retry.Delay = TimeSpan.Zero;
        }

        try
        {
            await call(provider.GetRequiredService<TContract>());
        }
        catch (HttpApiException)
        {
            // An error status is an outcome both paths should reach identically; the requests
            // that led there are what is compared.
        }

        return transport.Requests.Select(request => $"{transport.Name} {Describe(request)}")
            .Concat(extra?.Requests.Select(request => $"{extra.Name} {Describe(request)}") ?? [])
            .ToList();
    }

    private static string Describe(TransportRequest request)
        => $"{request.Method} {request.RequestUri} "
           + $"[{string.Join("; ", request.Headers.Select(header => $"{header.Key}={header.Value}"))}] "
           + $"{Describe(request.Content)} timeout={request.Timeout} capabilities={request.RequiredCapabilities}";

    private static string Describe(RequestContent? content) => content switch
    {
        null => "no body",
        BinaryContent binary => $"{binary.ContentType}:{Encoding.UTF8.GetString(binary.Bytes.Span)}",
        FormContent form => $"form:{string.Join("&", form.Fields.Select(field => $"{field.Key}={field.Value}"))}",
        MultipartFormContent multipart =>
            $"multipart:{string.Join(",", multipart.Parts.Select(part => $"{part.Name}/{part.FileName}/{Describe(part.Content)}"))}",
        _ => content.GetType().Name
    };

    private static TransportResponse WithHeader(TransportResponse response, string name, string value)
        => new(response.StatusCode)
        {
            Headers = [.. response.Headers, new KeyValuePair<string, string>(name, value)],
            Content = response.Content
        };
}
