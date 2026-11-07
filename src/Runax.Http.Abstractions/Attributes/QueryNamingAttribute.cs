namespace Runax.Http;

/// <summary>
/// Sets how query keys are derived from identifiers for every request on a client interface.
/// </summary>
/// <remarks>
/// <para>
/// For the API whose every key follows one convention — a Python backend spelling everything
/// <c>snake_case</c> being the usual reason. Set it once on the interface instead of renaming each
/// parameter and property by hand:
/// </para>
/// <code>
/// [BaseAddress("https://api.example.com")]
/// [QueryNaming(QueryNaming.SnakeCaseLower)]
/// public interface IReportClient
/// {
///     [Get("/reports")]
///     Task&lt;Report[]&gt; ListAsync([Query] int perPage, [Query] ReportFilter filter);  // ?per_page=…&amp;date_from=…
/// }
/// </code>
/// <para>
/// Overridden per parameter by <see cref="QueryAttribute.Naming"/>, and per field by an explicit
/// <c>[Query("name")]</c> — most specific wins, as elsewhere in this library.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class QueryNamingAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="QueryNamingAttribute"/> class.</summary>
    /// <param name="naming">The convention every query key on this client follows.</param>
    public QueryNamingAttribute(QueryNaming naming) => Naming = naming;

    /// <summary>The convention every query key on this client follows.</summary>
    public QueryNaming Naming { get; }
}
