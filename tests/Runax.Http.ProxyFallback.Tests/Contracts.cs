using Runax.Http.Transports;

namespace Runax.Http.ProxyFallback.Tests;

public sealed record Repository(string Name, int Stars);

public sealed record NewIssue(string Title);

public sealed record Issue(int Number, string Title);

public sealed record Problem(string Error);

/// <summary>Methods every client has, declared once and inherited.</summary>
public interface IHealthClient
{
    [Get("/health")]
    Task<ApiResponse> HealthAsync(CancellationToken ct = default);
}

/// <summary>
/// A client with no generated implementation, because this project does not run the generator.
/// </summary>
[BaseAddress("https://api.example.com/v3")]
[Header("User-Agent", "runax-proxy-tests")]
public interface IRepositoryClient : IHealthClient
{
    [Get("/repos/{owner}/{name}")]
    Task<Repository> GetAsync(string owner, string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}/issues")]
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string owner,
        [Path("name")] string repository,
        [Query] string? state = null,
        [Query] int perPage = 30,
        [Query] IEnumerable<string>? labels = null,
        CancellationToken ct = default);

    [Post("/repos/{owner}/{name}/issues")]
    [Retry(3, DelayMilliseconds = 0, AllowNonIdempotent = true)]
    Task<Issue> CreateIssueAsync(
        string owner,
        string name,
        [Body] NewIssue issue,
        [Header("Idempotency-Key")] Guid key,
        CancellationToken ct = default);

    [Delete("/repos/{owner}/{name}")]
    Task DeleteAsync(string owner, string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}")]
    Task<ApiResponse<Repository>> TryGetAsync(string owner, string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}/legacy")]
    [Response(typeof(Problem), WhenJsonProperty = "error")]
    [Response(typeof(Repository))]
    Task<ApiResult> GetLegacyAsync(string owner, string name, CancellationToken ct = default);

    [Post("/upload")]
    Task UploadAsync([Body(Format = BodyFormat.Multipart)] IEnumerable<MultipartPart> parts);
}

/// <summary>Declares a transport nothing registers, so startup validation has something to catch.</summary>
[BaseAddress("https://api.example.com")]
public interface IMisspelledTransportClient
{
    [Get("/ping")]
    [Transport("restsharpp")]
    Task PingAsync();
}

/// <summary>Every mistake at once, to prove they are all reported together.</summary>
[BaseAddress("not a uri")]
public interface IBrokenClient
{
    string Name { get; }

    [Get("/repos/{owner}/{name}")]
    Task<Repository> MissingPlaceholderAsync(string owner);

    [Get("/search")]
    Task<Repository> UnboundAsync(string q);

    [Get("/sync")]
    Repository SynchronousGet();

    [Get("/shape")]
    [Response(typeof(Repository))]
    Task<Repository> ResponseWithoutApiResultAsync();

    [Post("/form")]
    Task FormFromObjectAsync([Body(Format = BodyFormat.UrlEncoded)] NewIssue issue);

    [Get("/nested")]
    Task NestedQueryAsync([Query] Outer filter);

    [Get("/retry")]
    [Retry(0)]
    Task NoAttemptsAsync();

    Task NoAttributeAsync();
}

public sealed class Outer
{
    public Inner? Inner { get; init; }
}

public sealed class Inner
{
    public string? Value { get; init; }
}
