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
