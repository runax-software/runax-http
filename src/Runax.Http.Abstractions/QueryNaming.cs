namespace Runax.Http;

/// <summary>
/// How a query key is derived from a C# identifier — a parameter name, or a property name of an
/// expanded query object.
/// </summary>
/// <remarks>
/// <para>
/// Applied by the generator at compile time, so the key reaching the wire is a literal in the
/// emitted code. Nothing is transformed at run time.
/// </para>
/// <para>
/// An explicit <c>[Query("name")]</c> always wins over any policy. The policy exists for the case
/// where every key follows one convention — a Python or Ruby backend spelling everything
/// <c>snake_case</c>, say — so you set it once rather than renaming every field by hand.
/// </para>
/// <para>
/// Member names mirror <c>System.Text.Json</c>'s <c>JsonKnownNamingPolicy</c>, so the vocabulary
/// is one you already know from configuring body serialization.
/// </para>
/// </remarks>
public enum QueryNaming
{
    /// <summary>
    /// <c>perPage</c>, <c>state</c>. The default, because it matches both C# parameter naming and
    /// what the body serializer does to the same type.
    /// </summary>
    CamelCase = 0,

    /// <summary>
    /// The identifier exactly as written: a <c>PerPage</c> property sends <c>PerPage</c>.
    /// </summary>
    AsDeclared,

    /// <summary><c>per_page</c>, <c>state</c>.</summary>
    SnakeCaseLower,

    /// <summary><c>PER_PAGE</c>, <c>STATE</c>.</summary>
    SnakeCaseUpper,

    /// <summary><c>per-page</c>, <c>state</c>.</summary>
    KebabCaseLower,

    /// <summary><c>PER-PAGE</c>, <c>STATE</c>.</summary>
    KebabCaseUpper
}
