using System.Globalization;
using Microsoft.OpenApi;

namespace Runax.Http.OpenApi;

/// <summary>One interface method, ready to write.</summary>
internal sealed class OperationDefinition
{
    internal required string Name { get; init; }

    internal required string Verb { get; init; }

    internal required string Template { get; init; }

    internal required string ReturnType { get; init; }

    /// <summary>The name without Async, which inline types are named after.</summary>
    internal required string Stem { get; init; }

    internal required string Summary { get; init; }

    internal string? Description { get; init; }

    internal bool Deprecated { get; init; }

    internal List<string> Attributes { get; } = [];

    internal List<ParameterDefinition> Parameters { get; } = [];

    /// <summary>Things the generated method does not do that the document describes, written as comments.</summary>
    internal List<string> Notes { get; } = [];

    /// <summary>The types this method hands to or reads from the serializer, for the JSON context.</summary>
    internal List<CSharpType> Serialized { get; } = [];
}

/// <summary>One parameter, as written.</summary>
/// <param name="Declaration">Attribute, type, name and default: <c>[Query] int? limit = null</c>.</param>
/// <param name="Name">The identifier, for the <c>&lt;param&gt;</c> doc.</param>
/// <param name="Description">The parameter's description.</param>
internal sealed record ParameterDefinition(string Declaration, string Name, string? Description);

/// <summary>Turns each OpenAPI operation into an interface method.</summary>
/// <remarks>
/// Every decision here is one the Runax.Http generator would otherwise reject at build time, so the
/// rules are its rules: a path parameter is bound by name, a query key is spelled out only when the
/// generator would derive a different one, a query object must have only sendable properties, and a
/// form or multipart body is a sequence rather than a POCO.
/// </remarks>
internal sealed class OperationPlanner(SchemaTypes types, bool apiResult, List<string> warnings)
{
    internal const string TokenName = "cancellationToken";

    private readonly HashSet<string> _methodNames = new(StringComparer.Ordinal);

    internal bool UsesMultipart { get; private set; }

    internal bool UsesStream { get; private set; }

    internal OperationDefinition Plan(string path, HttpMethod method, OpenApiOperation operation, IOpenApiPathItem item)
    {
        var verb = Identifiers.Pascal(method.Method.ToLowerInvariant());
        var name = Identifiers.Unique(MethodName(operation, verb, path), _methodNames);
        var describedAs = $"{method.Method.ToUpperInvariant()} {path}";

        // Inline types are named after the operation without its Async: UpdateBookRequest.
        var stem = name.EndsWith("Async", StringComparison.Ordinal) ? name[..^"Async".Length] : name;

        var (returnType, responseAttributes, serialized, responseNote) = PlanResponse(operation, stem, describedAs);

        var definition = new OperationDefinition
        {
            Name = name,
            Verb = verb,
            Template = path,
            ReturnType = returnType,
            Stem = stem,
            Summary = operation.Summary ?? operation.Description?.Split('\n')[0] ?? describedAs,
            Description = operation.Description,
            Deprecated = operation.Deprecated
        };

        definition.Serialized.AddRange(serialized);
        definition.Attributes.AddRange(responseAttributes);

        if (responseNote is not null)
            definition.Notes.Add(responseNote);

        PlanParameters(definition, operation, item, describedAs);

        return definition;
    }

    private void PlanParameters(
        OperationDefinition definition,
        OpenApiOperation operation,
        IOpenApiPathItem item,
        string describedAs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { TokenName };
        var required = new List<ParameterDefinition>();
        var optional = new List<ParameterDefinition>();

        // Path-level parameters apply to every operation under the path; an operation's own
        // parameter with the same name and location replaces one.
        var parameters = new List<IOpenApiParameter>(item.Parameters ?? []);

        foreach (var own in operation.Parameters ?? [])
        {
            parameters.RemoveAll(inherited => inherited.Name == own.Name && inherited.In == own.In);
            parameters.Add(own);
        }

        // Path parameters first, in template order, so the signature reads like the URL.
        parameters = [.. parameters.OrderBy(parameter => parameter.In == ParameterLocation.Path
            ? definition.Template.IndexOf("{" + parameter.Name + "}", StringComparison.Ordinal)
            : int.MaxValue)];

        foreach (var parameter in parameters)
        {
            if (parameter.Name is not { Length: > 0 } wireName)
                continue;

            var hint = definition.Stem + Identifiers.Pascal(wireName);
            var type = types.Resolve(parameter.Schema, hint, out _);
            var identifier = Identifiers.Unique(Identifiers.Camel(wireName), names);
            string attribute;
            string fallback;

            switch (parameter.In)
            {
                case ParameterLocation.Path:
                    fallback = $"The {wireName} path segment.";

                    // An unannotated parameter binds to the placeholder of the same name.
                    attribute = identifier == wireName ? string.Empty : $"[Path({Identifiers.Literal(wireName)})] ";
                    type = Sendable(type, wireName, describedAs);
                    break;

                case ParameterLocation.Query:
                    fallback = $"The {wireName} query parameter.";
                    (attribute, type) = PlanQuery(parameter, type, identifier, describedAs, definition);
                    break;

                case ParameterLocation.Header:
                    fallback = $"The {wireName} header.";
                    attribute = $"[Header({Identifiers.Literal(wireName)})] ";
                    type = Sendable(type, wireName, describedAs);
                    break;

                default:
                    definition.Notes.Add(
                        $"The {parameter.In?.ToString().ToLowerInvariant()} parameter '{wireName}' is not "
                        + "supported and was left out.");
                    warnings.Add($"{describedAs}: the {parameter.In?.ToString().ToLowerInvariant()} parameter '{wireName}' was left out.");
                    continue;
            }

            var isRequired = parameter.Required || parameter.In == ParameterLocation.Path;
            var declaration = isRequired
                ? $"{attribute}{type.Name} {identifier}"
                : $"{attribute}{type.Nullable()} {identifier} = null";

            (isRequired ? required : optional).Add(
                new ParameterDefinition(declaration, identifier, Describe(parameter.Description ?? fallback, type)));
        }

        if (PlanBody(operation, definition, names, describedAs) is { } body)
            (body.Declaration.EndsWith("= null", StringComparison.Ordinal) ? optional : required).Add(body);

        // C# needs optional parameters after required ones; the token is always last.
        definition.Parameters.AddRange(required);
        definition.Parameters.AddRange(optional);
        definition.Parameters.Add(new ParameterDefinition(
            $"CancellationToken {TokenName} = default", TokenName, "Cancels the request."));
    }

    private (string Attribute, CSharpType Type) PlanQuery(
        IOpenApiParameter parameter,
        CSharpType type,
        string identifier,
        string describedAs,
        OperationDefinition definition)
    {
        var wireName = parameter.Name!;

        if (parameter.Style == ParameterStyle.DeepObject)
        {
            definition.Notes.Add(
                $"'{wireName}' is a deepObject query parameter, which has no attribute equivalent: put "
                + $"'{wireName}[field]' keys in this dictionary yourself.");

            return ("[Query] ", SchemaTypes.Dictionary(CSharpType.String));
        }

        if (type.Shape == TypeShape.Model)
        {
            if (type.Model!.IsQueryObject)
            {
                // Expanded to one key per property, which is what form style with explode — the
                // query default — means. The keys come from the record's own [Query] names.
                type.Model.UsedAsQuery = true;
                return ("[Query] ", type);
            }

            warnings.Add(
                $"{describedAs}: the query object '{wireName}' has properties that cannot be sent as query "
                + "values, so it is typed as a dictionary.");

            return ("[Query] ", SchemaTypes.Dictionary(CSharpType.String));
        }

        if (type.Shape == TypeShape.Dictionary)
            return ("[Query] ", SchemaTypes.Dictionary(CSharpType.String));

        if (type.Shape == TypeShape.List && parameter.Explode == false)
        {
            definition.Notes.Add(
                $"'{wireName}' is documented as comma-separated (explode: false), but a collection is sent as "
                + "repeated keys. If the API needs the comma form, take a string and join the values.");
        }

        type = type.Shape == TypeShape.List
            ? new CSharpType($"IEnumerable<{Sendable(type.Element!, wireName, describedAs).Name}>", false, TypeShape.List, type.Element)
            : Sendable(type, wireName, describedAs);

        // Only spelled out when the generator's camelCase of the parameter name would differ.
        return (Identifiers.DerivedQueryKey(identifier) == wireName
                ? "[Query] "
                : $"[Query({Identifiers.Literal(wireName)})] ",
            type);
    }

    private ParameterDefinition? PlanBody(
        OpenApiOperation operation,
        OperationDefinition definition,
        HashSet<string> names,
        string describedAs)
    {
        if (operation.RequestBody is not { Content: { Count: > 0 } content } body)
            return null;

        var (mediaType, media) = content.FirstOrDefault(entry => IsJson(entry.Key)) is { Key: not null } json
            ? json
            : content.First();

        var lowered = mediaType.Split(';')[0].Trim().ToLowerInvariant();
        var identifier = Identifiers.Unique("body", names);
        string declaration;
        string? description = body.Description;

        // Only a JSON body can be left out: the serializer writes null as null, while form, multipart
        // and raw bodies are built from the argument and have nothing to build from. Those are
        // required in the signature even where the document calls them optional — pass an empty one.
        if (IsJson(lowered))
        {
            var type = types.Resolve(media.Schema, definition.Stem + "Request", out _);
            definition.Serialized.Add(type);
            declaration = $"[Body] {Optional(type, body.Required, identifier)}";
        }
        else if (lowered == "application/x-www-form-urlencoded")
        {
            var form = SchemaTypes.Dictionary(CSharpType.String);
            declaration = $"[Body(Format = BodyFormat.UrlEncoded)] {form.Name} {identifier}";
            description = Fields(description, media.Schema, "Form fields");
        }
        else if (lowered == "multipart/form-data")
        {
            UsesMultipart = true;
            var parts = new CSharpType("IEnumerable<MultipartPart>", false, TypeShape.Opaque);
            declaration = $"[Body(Format = BodyFormat.Multipart)] {parts.Name} {identifier}";
            description = Fields(description, media.Schema, "Parts");
        }
        else
        {
            var text = lowered.StartsWith("text/", StringComparison.Ordinal);
            UsesStream |= !text;

            var raw = text
                ? CSharpType.String
                : new CSharpType("Stream", false, TypeShape.Opaque);

            declaration = $"[Body(Format = BodyFormat.Raw)] {raw.Name} {identifier}";

            // A raw string goes as text/plain and a stream as application/octet-stream unless told
            // otherwise; a constant header is how a method tells it.
            if (lowered != (text ? "text/plain" : "application/octet-stream"))
                definition.Attributes.Add($"[Header(\"Content-Type\", {Identifiers.Literal(mediaType)})]");
        }

        if (content.Count > 1)
        {
            definition.Notes.Add(
                $"The body is also accepted as {string.Join(", ", content.Keys.Where(key => key != mediaType))}; "
                + $"{mediaType} was chosen.");
        }

        if (definition.Verb is "Get" or "Head" or "Options")
            warnings.Add($"{describedAs} documents a request body; it is kept, and the generator will warn (RXH010).");

        return new ParameterDefinition(declaration, identifier, description ?? "The request body.");
    }

    private (string ReturnType, List<string> Attributes, List<CSharpType> Serialized, string? Note) PlanResponse(
        OpenApiOperation operation,
        string name,
        string describedAs)
    {
        var responses = (operation.Responses ?? new OpenApiResponses())
            .Select(entry => (Key: entry.Key, Response: entry.Value, Order: StatusOrder(entry.Key)))
            .OrderBy(entry => entry.Order)
            .ToList();

        if (apiResult)
        {
            var attributes = new List<string>();
            var serialized = new List<CSharpType>();
            string? fallback = null;

            foreach (var (key, response, _) in responses)
            {
                if (JsonSchema(response) is not { } schema)
                    continue;

                var type = types.Resolve(schema, name + Identifiers.Pascal(key) + "Response", out _);
                serialized.Add(type);

                switch (key.ToUpperInvariant())
                {
                    // Held back and added last: the unconditional response is the fallback, and
                    // anything after it is unreachable.
                    case "DEFAULT":
                        fallback = $"[Response(typeof({type.Name}))]";
                        break;

                    case [var first, 'X', 'X']:
                        attributes.Add(
                            $"[Response(typeof({type.Name}), MinStatusCode = {first}00, MaxStatusCode = {first}99)]");
                        break;

                    default:
                        attributes.Add($"[Response({key}, typeof({type.Name}))]");
                        break;
                }
            }

            if (fallback is not null)
                attributes.Add(fallback);

            if (attributes.Count > 0)
                return ("Task<ApiResult>", attributes, serialized, null);
        }

        var successes = responses.Where(entry => entry.Order < 300).ToList();

        if (successes.Count == 0)
            successes = responses.Where(entry => entry.Key == "default").ToList();

        foreach (var (key, response, _) in successes)
        {
            if (response.Content is not { Count: > 0 } content)
                continue;

            if (JsonSchema(response) is { } schema)
            {
                var type = types.Resolve(schema, name + "Response", out _);
                return ($"Task<{type.Name}>", [], [type], null);
            }

            // The serializer reads JSON; an image or a CSV has no type to deserialize into. The
            // status and headers are still worth having.
            return (
                "Task<ApiResponse>",
                [],
                [],
                $"The {key} response is {string.Join(", ", content.Keys)}, which the serializer cannot read, so "
                + "this returns the status and headers only.");
        }

        if (successes.Count == 0)
            warnings.Add($"{describedAs} documents no success response; it returns Task.");

        return ("Task", [], [], null);
    }

    private static IOpenApiSchema? JsonSchema(IOpenApiResponse response)
        => response.Content?.FirstOrDefault(entry => IsJson(entry.Key)).Value?.Schema;

    /// <summary>
    /// Sorts exact codes first, then ranges, then <c>default</c> — the order the generator matches
    /// <c>[Response]</c>s in, and the order a reader expects.
    /// </summary>
    private static double StatusOrder(string key)
        => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            ? code
            : key.Length == 3 && char.IsDigit(key[0])
                ? ((key[0] - '0') * 100) + 99.5
                : 1000;

    private CSharpType Sendable(CSharpType type, string wireName, string describedAs)
    {
        if (type.Shape == TypeShape.Scalar)
            return type;

        warnings.Add($"{describedAs}: '{wireName}' cannot be sent as text, so it is typed string.");
        return CSharpType.String;
    }

    private static string Optional(CSharpType type, bool required, string identifier)
        => required ? $"{type.Name} {identifier}" : $"{type.Nullable()} {identifier} = null";

    private static string? Fields(string? description, IOpenApiSchema? schema, string label)
    {
        var target = schema is OpenApiSchemaReference { Target: { } resolved } ? resolved : schema;

        if (target?.Properties is not { Count: > 0 } properties)
            return description;

        var fields = $"{label}: {string.Join(", ", properties.Keys)}.";
        return description is null ? fields : $"{description.TrimEnd('.')}. {fields}";
    }

    private static string? Describe(string? description, CSharpType type)
    {
        var values = type.Constants is { } constants
            ? $"One of the values in {constants.Name}."
            : type.EnumValues is { Count: > 0 } inline
                ? $"One of: {string.Join(", ", inline)}."
                : null;

        return (description, values) switch
        {
            (null, _) => values,
            (_, null) => description,
            _ => $"{description.TrimEnd('.')}. {values}"
        };
    }

    internal static bool IsJson(string mediaType)
    {
        var lowered = mediaType.Split(';')[0].Trim().ToLowerInvariant();

        return lowered is "application/json" or "text/json" or "*/*"
               || lowered.EndsWith("+json", StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>operationId</c> when the document has one; otherwise the verb and the path, with each
    /// placeholder as <c>By…</c>: <c>GET /pets/{id}</c> is <c>GetPetsById</c>.
    /// </summary>
    private static string MethodName(OpenApiOperation operation, string verb, string path)
    {
        var stem = operation.OperationId is { Length: > 0 } id
            ? Identifiers.Pascal(id)
            : verb + string.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.StartsWith('{')
                    ? "By" + Identifiers.Pascal(segment.Trim('{', '}'))
                    : Identifiers.Pascal(segment)));

        return stem.EndsWith("Async", StringComparison.Ordinal) ? stem : stem + "Async";
    }
}
