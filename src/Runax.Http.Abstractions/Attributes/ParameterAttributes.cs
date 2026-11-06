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

/// <summary>
/// Binds a parameter to the request body. At most one parameter per method may carry this.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute
{
    /// <summary>How the value is encoded onto the wire. Defaults to <see cref="BodyFormat.Json"/>.</summary>
    public BodyFormat Format { get; set; } = BodyFormat.Json;
}

/// <summary>How a <see cref="BodyAttribute"/>-annotated parameter is encoded.</summary>
public enum BodyFormat
{
    /// <summary>Serialized by the configured <see cref="Runax.Http.Serialization.IContentSerializer"/>.</summary>
    Json,

    /// <summary><c>application/x-www-form-urlencoded</c>.</summary>
    UrlEncoded,

    /// <summary><c>multipart/form-data</c>.</summary>
    Multipart,

    /// <summary>Sent as-is: <see cref="string"/>, <see cref="byte"/> array, or <see cref="System.IO.Stream"/>.</summary>
    Raw
}

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
