namespace Runax.Http;

/// <summary>
/// Binds a parameter to a query string value. The parameter name is used unless
/// <see cref="Name"/> is set. Collections are expanded to repeated keys.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
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
