using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Runax.Http.OpenApi;

/// <summary>
/// Maps OpenAPI schemas to C# types, creating a record for each object schema it meets.
/// </summary>
/// <remarks>
/// <para>
/// Named schemas from <c>components</c> are resolved once and shared, so two operations returning
/// <c>Pet</c> return the same record, and a schema that refers to itself terminates. An inline
/// object gets a record named after where it was found — <c>PetOwner</c> for the <c>owner</c>
/// property of <c>Pet</c>.
/// </para>
/// <para>
/// Composition is flattened rather than modelled: <c>allOf</c> merges its parts into one record,
/// since a sealed record with every property is what a JSON body needs; <c>oneOf</c> and
/// <c>anyOf</c> become <c>JsonElement</c> unless they only add <c>null</c>, because choosing among
/// alternatives needs a discriminator the serializer would have to be taught about.
/// </para>
/// </remarks>
internal sealed class SchemaTypes(ISet<string> typeNames, List<string> warnings)
{
    private readonly Dictionary<string, CSharpType> _components = new(StringComparer.Ordinal);

    internal List<ModelDefinition> Models { get; } = [];

    internal List<ConstantsDefinition> Constants { get; } = [];

    /// <summary>Resolves every named schema, in document order, so the output follows the document.</summary>
    internal void AddComponents(IDictionary<string, IOpenApiSchema>? schemas)
    {
        if (schemas is null)
            return;

        foreach (var (id, schema) in schemas)
            Component(id, schema);
    }

    /// <summary>The C# type for a schema.</summary>
    /// <param name="schema">The schema, or null where the document gave none.</param>
    /// <param name="hint">What to name a record created for an inline object.</param>
    /// <param name="nullable">Whether the schema admits <c>null</c>.</param>
    internal CSharpType Resolve(IOpenApiSchema? schema, string hint, out bool nullable)
    {
        nullable = false;

        if (schema is null)
            return CSharpType.Json;

        nullable = schema.Type is { } declared && declared.HasFlag(JsonSchemaType.Null);

        if (schema is OpenApiSchemaReference reference)
        {
            if (reference.Reference?.Id is not { } id || reference.Target is null)
            {
                warnings.Add($"A reference to '{reference.Reference?.ReferenceV3}' could not be resolved; it is typed JsonElement.");
                return CSharpType.Json;
            }

            return Component(id, reference.Target);
        }

        if (IsModel(schema))
            return Model(Identifiers.Pascal(hint), schema);

        if (schema.AllOf is { Count: 1 } single)
        {
            var inner = Resolve(single[0], hint, out var innerNullable);
            nullable |= innerNullable;
            return inner;
        }

        var alternatives = (schema.OneOf ?? []).Concat(schema.AnyOf ?? []).ToList();

        if (alternatives.Count > 0)
        {
            // OpenAPI 3.1's way of saying "nullable reference" is anyOf: [$ref, {type: null}], so a
            // null alternative is nullability, not a second shape.
            var shapes = alternatives.Where(alternative => alternative.Type != JsonSchemaType.Null).ToList();
            nullable |= shapes.Count < alternatives.Count;

            if (shapes.Count != 1)
                return CSharpType.Json;

            var inner = Resolve(shapes[0], hint, out var innerNullable);
            nullable |= innerNullable;
            return inner;
        }

        var type = (schema.Type ?? 0) & ~JsonSchemaType.Null;

        return type switch
        {
            JsonSchemaType.String => StringType(schema),
            JsonSchemaType.Integer => schema.Format == "int64"
                ? new CSharpType("long", true, TypeShape.Scalar)
                : new CSharpType("int", true, TypeShape.Scalar),
            JsonSchemaType.Number => schema.Format switch
            {
                "float" => new CSharpType("float", true, TypeShape.Scalar),
                "decimal" => new CSharpType("decimal", true, TypeShape.Scalar),
                _ => new CSharpType("double", true, TypeShape.Scalar)
            },
            JsonSchemaType.Boolean => new CSharpType("bool", true, TypeShape.Scalar),
            JsonSchemaType.Array => List(Resolve(schema.Items, hint + "Item", out _)),
            JsonSchemaType.Object when schema.AdditionalProperties is { } values =>
                Dictionary(Resolve(values, hint + "Value", out _)),

            // A free-form object, a schema with no type, or several types at once: all things a
            // JsonElement represents losslessly and a guess would not.
            _ => CSharpType.Json
        };
    }

    internal static CSharpType Dictionary(CSharpType value)
        => new($"IReadOnlyDictionary<string, {value.Name}>", false, TypeShape.Dictionary, value);

    internal static CSharpType List(CSharpType element)
        => new($"IReadOnlyList<{element.Name}>", false, TypeShape.List, element);

    private CSharpType Component(string id, IOpenApiSchema schema)
    {
        if (_components.TryGetValue(id, out var known))
            return known;

        if (IsModel(schema))
        {
            var model = NewModel(Identifiers.Pascal(id), schema);
            var type = new CSharpType(model.Name, false, TypeShape.Model, Model: model);

            // Registered before its properties are read, so a schema that refers to itself — a tree,
            // a linked list — resolves to the record being built instead of recursing forever.
            _components[id] = type;
            Fill(model, schema);

            return type;
        }

        if (StringEnumValues(schema) is { } values)
        {
            var constants = new ConstantsDefinition(
                Identifiers.Unique(Identifiers.Pascal(id), typeNames), values, schema.Description);

            Constants.Add(constants);

            return _components[id] = CSharpType.String with { Constants = constants };
        }

        return _components[id] = Resolve(schema, id, out _);
    }

    private CSharpType Model(string name, IOpenApiSchema schema)
    {
        var model = NewModel(name, schema);
        Fill(model, schema);

        return new CSharpType(model.Name, false, TypeShape.Model, Model: model);
    }

    private ModelDefinition NewModel(string name, IOpenApiSchema schema)
    {
        var model = new ModelDefinition(Identifiers.Unique(name, typeNames))
        {
            Description = schema.Description,
            Deprecated = schema.Deprecated
        };

        Models.Add(model);

        return model;
    }

    private void Fill(ModelDefinition model, IOpenApiSchema schema)
    {
        var properties = new List<KeyValuePair<string, IOpenApiSchema>>();
        var required = new HashSet<string>(StringComparer.Ordinal);

        Collect(schema, properties, required, depth: 0);

        // A property cannot share its record's name (CS0542), and two wire names can collapse to one
        // identifier — "petId" and "pet_id" — so names are made unique within the record.
        var members = new HashSet<string>(StringComparer.Ordinal) { model.Name };

        foreach (var (wireName, propertySchema) in properties)
        {
            var name = Identifiers.Pascal(wireName);
            var type = Resolve(propertySchema, model.Name + name, out var nullable);

            model.Properties.Add(new PropertyDefinition(
                Identifiers.Unique(name, members),
                wireName,
                type,

                // A read-only property is one the server fills in. Required in a response, it would
                // still have to be invented by every caller building a request, so it never is.
                Required: required.Contains(wireName) && !propertySchema.ReadOnly,
                Nullable: nullable,
                propertySchema.Description,
                propertySchema.Deprecated));
        }
    }

    /// <summary>Gathers properties through <c>allOf</c>, later declarations overriding earlier ones.</summary>
    private static void Collect(
        IOpenApiSchema schema,
        List<KeyValuePair<string, IOpenApiSchema>> properties,
        HashSet<string> required,
        int depth)
    {
        // allOf chains can be cyclic in a malformed document; a real one is never this deep.
        if (depth > 32)
            return;

        if (schema is OpenApiSchemaReference { Target: { } target })
            schema = target;

        foreach (var part in schema.AllOf ?? [])
            Collect(part, properties, required, depth + 1);

        foreach (var (name, property) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            var existing = properties.FindIndex(entry => entry.Key == name);
            var entry = new KeyValuePair<string, IOpenApiSchema>(name, property);

            if (existing >= 0)
                properties[existing] = entry;
            else
                properties.Add(entry);
        }

        required.UnionWith(schema.Required ?? new HashSet<string>());
    }

    /// <summary>Whether a schema becomes a record: it has properties, or merges schemas that do.</summary>
    private static bool IsModel(IOpenApiSchema schema)
        => schema is not OpenApiSchemaReference
           && ((schema.Properties?.Count ?? 0) > 0
               || (schema.AllOf?.Count ?? 0) > 1
               || ((schema.AllOf?.Count ?? 0) == 1 && (schema.Properties?.Count ?? 0) > 0));

    private static CSharpType StringType(IOpenApiSchema schema) => schema.Format switch
    {
        "date-time" => new CSharpType("DateTimeOffset", true, TypeShape.Scalar),
        "date" => new CSharpType("DateOnly", true, TypeShape.Scalar),
        "time" => new CSharpType("TimeOnly", true, TypeShape.Scalar),
        "uuid" => new CSharpType("Guid", true, TypeShape.Scalar),
        "uri" => new CSharpType("Uri", false, TypeShape.Scalar),
        "byte" or "binary" => CSharpType.Bytes,
        _ => StringEnumValues(schema) is { } values ? CSharpType.String with { EnumValues = values } : CSharpType.String
    };

    private static List<string>? StringEnumValues(IOpenApiSchema schema)
    {
        if (schema.Enum is not { Count: > 0 } values || ((schema.Type ?? 0) & ~JsonSchemaType.Null) != JsonSchemaType.String)
            return null;

        return values
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .OfType<string>()
            .ToList();
    }
}
