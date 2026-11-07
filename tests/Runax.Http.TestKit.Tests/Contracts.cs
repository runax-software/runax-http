namespace Runax.Http.TestKit.Tests;

internal sealed record Repository(string Name, int Stars);
internal sealed record NewIssue(string Title);
internal sealed record Issue(int Number, string Title);

/// <summary>A client with no [BaseAddress], so the harness's default has something to prove.</summary>
internal interface IRepositoryClient
{
    [Get("/repos/{owner}/{name}")]
    Task<Repository> GetAsync(string owner, string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}/issues")]
    Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string owner,
        string name,
        [Query] string state = "open",
        [Query] IEnumerable<string>? labels = null,
        CancellationToken ct = default);

    [Post("/repos/{owner}/{name}/issues")]
    Task<Issue> CreateIssueAsync(
        string owner,
        string name,
        [Body] NewIssue issue,
        [Header("Authorization")] string authorization,
        CancellationToken ct = default);

    [Delete("/repos/{owner}/{name}")]
    Task DeleteAsync(string owner, string name, CancellationToken ct = default);

    [Get("/repos/{owner}/{name}")]
    Task<ApiResponse<Repository>> TryGetAsync(string owner, string name, CancellationToken ct = default);
}
