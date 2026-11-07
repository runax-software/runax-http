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
/// On a property: renames that field when its declaring type is used as a query object, and wins
/// over any <see cref="QueryNaming"/> policy. Fields are otherwise named by the policy in force,
/// which is <see cref="QueryNaming.CamelCase"/> unless the parameter or the interface says
/// otherwise.
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

    /// <summary>The query key, or <see langword="null"/> to derive it from the identifier.</summary>
    public string? Name { get; }

    /// <summary>
    /// How this parameter's key — and the keys of an expanded query object's fields — are derived
    /// from their identifiers. Overrides <see cref="QueryNamingAttribute"/> on the interface.
    /// </summary>
    /// <remarks>
    /// For the one endpoint whose query convention differs from the rest of its client. An explicit
    /// <see cref="Name"/>, here or on a property, still wins over this.
    /// </remarks>
    public QueryNaming Naming { get; set; } = QueryNaming.CamelCase;
}
