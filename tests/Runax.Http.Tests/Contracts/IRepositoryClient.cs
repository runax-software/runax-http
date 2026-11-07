namespace Runax.Http.Tests.Contracts;

/// <summary>A repository, as the fake API returns it.</summary>
internal sealed record Repository(string Name, int Stars);

/// <summary>An issue, as the fake API returns it.</summary>
internal sealed record Issue(int Number, string Title);

/// <summary>A new issue, as the fake API accepts it.</summary>
internal sealed record NewIssue(string Title, string? Body = null);

/// <summary>
/// The client interface the generator is tested against. Deliberately exercises one of everything
/// the generator claims to support, so a regression in any binding shows up as a failing test
/// rather than being noticed by a consumer.
/// </summary>
[BaseAddress("https://api.example.com/v3")]
[Header("User-Agent", "runax-http-tests")]
internal interface IRepositoryClient
{
    [Get("/repos/{owner}/{name}")]
    Task<Repository> GetAsync(string owner, string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}/issues")]
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        [Path] string owner,
        [Path("name")] string repository,
        [Query] string state = "open",
        [Query("per_page")] int perPage = 30,
        [Query] IEnumerable<string>? labels = null,
        CancellationToken ct = default);

    [Post("/repos/{owner}/{name}/issues")]
    Task<Issue> CreateIssueAsync(
        string owner,
        string name,
        [Body] NewIssue issue,
        CancellationToken ct = default);

    [Delete("/repos/{owner}/{name}")]
    Task DeleteAsync(string owner, string name, CancellationToken ct = default);

    /// <summary>A 404 here means "no such repository", which is an answer rather than a fault.</summary>
    [Get("/repos/{owner}/{name}")]
    Task<ApiResponse<Repository>> TryGetAsync(string owner, string name, CancellationToken ct = default);

    [Head("/repos/{owner}/{name}")]
    Task<ApiResponse> ExistsAsync(string owner, string name, CancellationToken ct = default);

    [Get("/user")]
    Task<Repository> GetWithHeaderAsync(
        [Header("Authorization")] string authorization,
        [Header("X-Trace-Id")] Guid traceId,
        CancellationToken ct = default);

    [Put("/repos/{owner}/{name}/readme")]
    [Header("Content-Type", "text/markdown")]
    Task PutReadmeAsync(
        string owner,
        string name,
        [Body(Format = BodyFormat.Raw)] string markdown,
        CancellationToken ct = default);
}

/// <summary>
/// A client whose methods declare their own retry policy, for the precedence tests.
/// </summary>
/// <remarks>
/// Separate from <see cref="IRepositoryClient"/> so the other suites are not quietly retrying: a
/// declaration turns retrying on without the operator asking, which is the point of it and would
/// be noise everywhere else.
/// </remarks>
[BaseAddress("https://api.example.com/v3")]
internal interface IFlakyClient
{
    /// <summary>Declares more attempts than the client-wide default in these tests.</summary>
    [Get("/reports/{id}")]
    [Retry(4)]
    Task<Repository> GetReportAsync(int id, CancellationToken ct = default);

    /// <summary>Declares a base delay too, so the delay override is observable.</summary>
    [Get("/reports/{id}/slow")]
    [Retry(3, DelayMilliseconds = 0)]
    Task<Repository> GetSlowReportAsync(int id, CancellationToken ct = default);

    /// <summary>Exempts itself from a client-wide retry policy.</summary>
    [Get("/reports/{id}/once")]
    [Retry(1)]
    Task<Repository> GetOnceAsync(int id, CancellationToken ct = default);

    /// <summary>Opts a non-idempotent method in explicitly.</summary>
    [Post("/reports")]
    [Retry(3, AllowNonIdempotent = true)]
    Task<Repository> CreateReportAsync([Body] NewIssue report, CancellationToken ct = default);

    /// <summary>Declares nothing, so the client's own settings apply unchanged.</summary>
    [Get("/reports")]
    Task<Repository> ListReportsAsync(CancellationToken ct = default);
}
