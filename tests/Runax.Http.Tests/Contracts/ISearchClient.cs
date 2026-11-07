namespace Runax.Http.Tests.Contracts;

/// <summary>Pagination fields shared by filters, to prove an inherited property is expanded.</summary>
internal abstract record Paged
{
    public int Page { get; init; } = 1;

    [Query("per_page")]
    public int PerPage { get; init; } = 30;
}

/// <summary>A flat filter object — the shape that motivates expansion.</summary>
internal sealed record SearchFilter : Paged
{
    public string? State { get; init; }

    [Query("sort_by")]
    public string? SortBy { get; init; }

    public IEnumerable<string>? Labels { get; init; }

    public DateTimeOffset? Since { get; init; }

    public bool? Archived { get; init; }

    public SearchOrder Order { get; init; }
}

/// <summary>Proves an enum property sends its member name, as an enum parameter already does.</summary>
internal enum SearchOrder
{
    Ascending,
    Descending
}

/// <summary>A struct filter, so the non-nullable value-type access path is exercised.</summary>
internal readonly record struct PointFilter(int X, int Y);

/// <summary>Query objects, dictionaries, and the scalars that already worked.</summary>
[BaseAddress("https://api.example.com")]
internal interface ISearchClient
{
    [Get("/search")]
    Task<Widget> SearchAsync([Query] SearchFilter filter, CancellationToken ct = default);

    [Get("/search/nullable")]
    Task<Widget> SearchNullableAsync([Query] SearchFilter? filter, CancellationToken ct = default);

    [Get("/search/point")]
    Task<Widget> SearchPointAsync([Query] PointFilter point, CancellationToken ct = default);

    [Get("/search/struct-nullable")]
    Task<Widget> SearchNullablePointAsync([Query] PointFilter? point, CancellationToken ct = default);

    [Get("/search/dynamic")]
    Task<Widget> SearchDynamicAsync(
        [Query] Dictionary<string, string?> filters,
        CancellationToken ct = default);

    [Get("/search/dynamic-typed")]
    Task<Widget> SearchDynamicTypedAsync(
        [Query] IEnumerable<KeyValuePair<string, object?>> filters,
        CancellationToken ct = default);

    /// <summary>An object alongside a plain scalar, which must keep working as before.</summary>
    [Get("/search/mixed")]
    Task<Widget> SearchMixedAsync(
        [Query] string q,
        [Query] SearchFilter filter,
        CancellationToken ct = default);
}

/// <summary>A filter whose property names exercise every word boundary the policies split on.</summary>
internal sealed class ReportFilter
{
    public string? State { get; set; }

    public int PerPage { get; set; }

    /// <summary>An acronym run, which must stay one word rather than becoming <c>h_t_t_p</c>.</summary>
    public string? HTTPReferer { get; set; }

    /// <summary>A digit run, which is its own word.</summary>
    public int Top10Items { get; set; }

    /// <summary>Explicitly named, so no policy may touch it.</summary>
    [Query("EXACT")]
    public string? Exact { get; set; }
}

/// <summary>
/// A client against an API that spells every key <c>snake_case</c> — a Python or Ruby backend,
/// typically.
/// </summary>
[BaseAddress("https://api.example.com")]
[QueryNaming(QueryNaming.SnakeCaseLower)]
internal interface ISnakeCaseClient
{
    [Get("/reports")]
    Task<Widget> ListAsync(
        [Query] ReportFilter filter,
        [Query] int pageNumber,
        CancellationToken ct = default);

    /// <summary>The one endpoint whose convention differs from its client's.</summary>
    [Get("/legacy")]
    Task<Widget> LegacyAsync(
        [Query(Naming = QueryNaming.AsDeclared)] ReportFilter filter,
        CancellationToken ct = default);
}

/// <summary>A client taking the default policy, for contrast.</summary>
[BaseAddress("https://api.example.com")]
internal interface IDefaultNamingClient
{
    [Get("/reports")]
    Task<Widget> ListAsync([Query] ReportFilter filter, CancellationToken ct = default);
}
