namespace Runax.Http;

/// <summary>
/// Adds a header. On an interface or method the value is constant; on a parameter the
/// argument supplies the value at call time.
/// </summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class HeaderAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="HeaderAttribute"/> class for a parameter.</summary>
    /// <param name="name">The header name.</param>
    public HeaderAttribute(string name) => Name = name;

    /// <summary>Initializes a new instance of the <see cref="HeaderAttribute"/> class with a constant value.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The constant header value.</param>
    public HeaderAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The header name.</summary>
    public string Name { get; }

    /// <summary>The constant header value, or <see langword="null"/> when a parameter supplies it.</summary>
    public string? Value { get; }
}
