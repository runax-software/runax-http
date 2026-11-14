namespace Runax.Http.OpenApi;

/// <summary>What kind of C# type a schema became, which decides where it may appear.</summary>
internal enum TypeShape
{
    /// <summary>Something <c>RequestValueFormatter</c> can turn into text: a string, number, date, Guid, Uri.</summary>
    Scalar,

    /// <summary>A generated record.</summary>
    Model,

    /// <summary><c>IReadOnlyList&lt;T&gt;</c>.</summary>
    List,

    /// <summary><c>IReadOnlyDictionary&lt;string, T&gt;</c>.</summary>
    Dictionary,

    /// <summary>
    /// Bytes, a stream, or <c>JsonElement</c> — anything a body can carry but a query string
    /// cannot.
    /// </summary>
    Opaque
}

/// <summary>A C# type, as written in the generated source.</summary>
/// <param name="Name">The type as written, without a nullable marker.</param>
/// <param name="IsValueType">Whether <c>?</c> makes it <see cref="Nullable{T}"/> rather than an annotation.</param>
/// <param name="Shape">What kind of type it is.</param>
/// <param name="Element">The element type of a list or the value type of a dictionary.</param>
/// <param name="Model">The record, when this is one.</param>
/// <param name="Constants">The constants class listing a string enum's values, when there is one.</param>
/// <param name="EnumValues">An inline enum's values, for the doc comment.</param>
internal sealed record CSharpType(
    string Name,
    bool IsValueType,
    TypeShape Shape,
    CSharpType? Element = null,
    ModelDefinition? Model = null,
    ConstantsDefinition? Constants = null,
    IReadOnlyList<string>? EnumValues = null)
{
    internal static readonly CSharpType String = new("string", false, TypeShape.Scalar);
    internal static readonly CSharpType Json = new("JsonElement", true, TypeShape.Opaque);
    internal static readonly CSharpType Bytes = new("byte[]", false, TypeShape.Opaque);

    /// <summary>Whether a <c>[Query]</c> parameter of this type, or a query object property, can be sent.</summary>
    internal bool IsQueryValue => Shape == TypeShape.Scalar
                                  || (Shape == TypeShape.List && Element!.Shape == TypeShape.Scalar);

    internal string Nullable() => Name + "?";
}

/// <summary>A record generated for an object schema.</summary>
internal sealed class ModelDefinition(string name)
{
    internal string Name { get; } = name;

    internal string? Description { get; set; }

    internal bool Deprecated { get; set; }

    internal List<PropertyDefinition> Properties { get; } = [];

    /// <summary>
    /// Set when an operation takes this type as a <c>[Query]</c> object, so each property also
    /// carries the key the generator should send — it reads <c>[Query]</c>, not <c>[JsonPropertyName]</c>.
    /// </summary>
    internal bool UsedAsQuery { get; set; }

    /// <summary>Whether every property can be sent as a query value, which the generator requires.</summary>
    internal bool IsQueryObject => Properties.Count > 0 && Properties.All(property => property.Type.IsQueryValue);
}

/// <summary>One property of a generated record.</summary>
internal sealed record PropertyDefinition(
    string Name,
    string WireName,
    CSharpType Type,
    bool Required,
    bool Nullable,
    string? Description,
    bool Deprecated);

/// <summary>
/// The values of a named string enum, as constants.
/// </summary>
/// <remarks>
/// A string rather than a C# enum on purpose. The JSON spelling of an enum member and the spelling
/// <c>RequestValueFormatter</c> sends in a query string are both the member name, so a C# enum would
/// put <c>Available</c> on the wire where the API documents <c>available</c> — or need converters and
/// attributes that differ between .NET 8 and 9. A string is exactly what the API takes on every path,
/// and the constants keep the values discoverable.
/// </remarks>
internal sealed class ConstantsDefinition(string name, IReadOnlyList<string> values, string? description)
{
    internal string Name { get; } = name;

    internal IReadOnlyList<string> Values { get; } = values;

    internal string? Description { get; } = description;
}
