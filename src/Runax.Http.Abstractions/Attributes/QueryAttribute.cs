namespace Runax.Http;

/// <summary>
/// Binds a parameter to a query string value, or renames a property of an expanded query object.
/// </summary>
/// <remarks>
/// <para>
/// On a parameter: the parameter name is used unless <see cref="Name"/> is set, and a collection is
/// expanded to repeated keys.
/// </para>
/// <para>
/// On a property: renames that field when its declaring type is used as a query object. A query
/// object's fields are otherwise named after their properties exactly as written, because nothing
/// in this library silently transforms a name — so this is how a C# <c>PerPage</c> reaches an API
/// that spells it <c>per_page</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class QueryAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="QueryAttribute"/> class.</summary>
    public QueryAttribute() { }

    /// <summary>Initializes a new instance of the <see cref="QueryAttribute"/> class.</summary>
    /// <param name="name">The query key to use instead of the parameter name.</param>
    public QueryAttribute(string name) => Name = name;

    /// <summary>The query key, or <see langword="null"/> to use the parameter name.</summary>
    public string? Name { get; }
}
