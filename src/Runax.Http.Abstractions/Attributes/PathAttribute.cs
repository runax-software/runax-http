namespace Runax.Http;

/// <summary>
/// Binds a parameter to a <c>{placeholder}</c> in the method's path template. The parameter
/// name is used unless <see cref="Name"/> is set.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class PathAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="PathAttribute"/> class.</summary>
    public PathAttribute() { }

    /// <summary>Initializes a new instance of the <see cref="PathAttribute"/> class.</summary>
    /// <param name="name">The placeholder name to fill instead of the parameter name.</param>
    public PathAttribute(string name) => Name = name;

    /// <summary>The placeholder name, or <see langword="null"/> to use the parameter name.</summary>
    public string? Name { get; }
}
