using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace Runax.Http.OpenApi;

/// <summary>What to generate, beyond the document itself.</summary>
/// <param name="Namespace">The namespace, or null to derive one from the title.</param>
/// <param name="ClientName">The client's name without the I or the Client suffix, or null to derive it.</param>
/// <param name="ApiResult">Return <c>ApiResult</c> with a <c>[Response]</c> per documented response.</param>
/// <param name="Source">Where the document came from, for the header comment.</param>
/// <param name="SourceUri">The document's URL when it was fetched from one, to resolve a relative server against.</param>
internal sealed record GeneratorOptions(
    string? Namespace,
    string? ClientName,
    bool ApiResult,
    string Source,
    Uri? SourceUri = null);

/// <summary>One generated source file.</summary>
internal sealed record GeneratedFile(string Name, string Content);

/// <summary>Everything one run produces.</summary>
internal sealed record GenerationResult(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<string> Warnings,
    string Namespace,
    string InterfaceName,
    string? JsonContextName);

/// <summary>
/// Turns an OpenAPI document into an annotated client interface, the records it uses, and a
/// <c>JsonSerializerContext</c> covering what it serializes.
/// </summary>
/// <remarks>
/// The output is input to the Runax.Http generator, not a client in its own right: the interface is
/// annotated exactly as someone would write it by hand, so the generator implements it and checks
/// it — including that the JSON context covers every type the interface sends and receives.
/// </remarks>
internal static partial class ClientGenerator
{
    /// <summary>
    /// Type names a schema must not take, because the generated code refers to them unqualified. A
    /// schema called <c>Task</c> would otherwise break every method signature in the interface.
    /// </summary>
    private static readonly string[] Reserved =
    [
        "Task", "CancellationToken", "Stream", "Uri", "Guid", "DateTimeOffset", "DateOnly", "TimeOnly",
        "JsonElement", "ApiResponse", "ApiResult", "MultipartPart", "IReadOnlyList", "IReadOnlyDictionary",
        "IEnumerable", "BodyFormat", "Obsolete"
    ];

    internal static GenerationResult Generate(OpenApiDocument document, GeneratorOptions options)
    {
        var warnings = new List<string>();
        var title = document.Info?.Title is { Length: > 0 } declared ? declared : "Api";
        var stem = options.ClientName is { Length: > 0 } named ? Identifiers.Pascal(named) : Stem(title);
        var ns = options.Namespace is { Length: > 0 } given ? given : stem;
        var interfaceName = $"I{stem}Client";
        var contextName = $"{stem}JsonContext";

        var typeNames = new HashSet<string>(Reserved, StringComparer.Ordinal) { interfaceName, contextName };
        var types = new SchemaTypes(typeNames, warnings);
        var planner = new OperationPlanner(types, options.ApiResult, warnings);

        types.AddComponents(document.Components?.Schemas);

        var operations = new List<OperationDefinition>();

        foreach (var (path, item) in document.Paths ?? new OpenApiPaths())
        {
            foreach (var (method, operation) in item.Operations ?? [])
                operations.Add(planner.Plan(path, method, operation, item));
        }

        if (operations.Count == 0)
            warnings.Add("The document describes no operations, so the interface has no methods.");

        var header = Header(document, options.Source);
        var serialized = operations
            .SelectMany(operation => operation.Serialized)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var files = new List<GeneratedFile>
        {
            new($"{interfaceName}.cs", WriteInterface(document, options.SourceUri, header, ns, interfaceName, operations, warnings,
                serialized.Count > 0 ? contextName : null))
        };

        if (types.Models.Count > 0 || types.Constants.Count > 0)
            files.Add(new GeneratedFile($"{stem}Models.cs", WriteModels(header, ns, types)));

        if (serialized.Count > 0)
            files.Add(new GeneratedFile($"{contextName}.cs", WriteContext(header, ns, contextName, interfaceName, serialized)));

        return new GenerationResult(files, warnings, ns, interfaceName, serialized.Count > 0 ? contextName : null);
    }

    /// <summary><c>Petstore API</c> becomes <c>Petstore</c>, and so does <c>Petstore Client</c>.</summary>
    private static string Stem(string title)
    {
        var stem = Identifiers.Pascal(title);

        foreach (var suffix in new[] { "Client", "API", "Api" })
        {
            if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.Ordinal))
                stem = stem[..^suffix.Length];
        }

        return stem;
    }

    private static string Header(OpenApiDocument document, string source)
    {
        var described = document.Info is { } info
            ? $" — {info.Title}{(info.Version is { Length: > 0 } version ? " " + version : string.Empty)}"
            : string.Empty;

        return $"""
                // Generated by dotnet-runax-http from {Path.GetFileName(source)}{described}.
                // A starting point you own: edit it freely. Running the tool again leaves it alone unless
                // you pass --force.

                #nullable enable
                """;
    }

    private static string WriteInterface(
        OpenApiDocument document,
        Uri? source,
        string header,
        string ns,
        string interfaceName,
        List<OperationDefinition> operations,
        List<string> warnings,
        string? contextName)
    {
        var body = new StringBuilder();

        body.AppendLine("/// <summary>");

        foreach (var line in Identifiers.DocLines(document.Info?.Title ?? interfaceName))
            body.AppendLine(CultureInfo.InvariantCulture, $"/// {line}");

        body.AppendLine("/// </summary>");

        var remarks = new List<string>(Identifiers.DocLines(document.Info?.Description));
        var baseAddress = BaseAddress(document, source, warnings, remarks);

        remarks.AddRange(Authentication(document));

        if (contextName is not null)
        {
            remarks.Add(
                $"Serialize with <see cref=\"{contextName}\"/>: "
                + $"<c>services.AddRunaxHttp().UseSystemTextJson({contextName}.Default.Options)</c>.");
        }

        WriteRemarks(body, string.Empty, remarks);

        if (baseAddress is not null)
            body.AppendLine(CultureInfo.InvariantCulture, $"[BaseAddress({Identifiers.Literal(baseAddress)})]");

        body.AppendLine(CultureInfo.InvariantCulture, $"public interface {interfaceName}");
        body.AppendLine("{");

        for (var i = 0; i < operations.Count; i++)
        {
            if (i > 0)
                body.AppendLine();

            WriteOperation(body, operations[i]);
        }

        body.AppendLine("}");

        return Assemble(header, ns, body.ToString(), "Runax.Http");
    }

    private static void WriteOperation(StringBuilder body, OperationDefinition operation)
    {
        const string indent = "    ";

        foreach (var note in operation.Notes)
            body.AppendLine(CultureInfo.InvariantCulture, $"{indent}// TODO: {note}");

        WriteSummary(body, indent, operation.Summary);

        if (operation.Description is { } description && description.Trim() != operation.Summary.Trim())
            WriteRemarks(body, indent, [.. Identifiers.DocLines(description)]);

        // Every parameter, always: documenting some but not others is CS1573 in any project that
        // generates documentation, and a warning in someone else's build is an error in many.
        foreach (var parameter in operation.Parameters)
        {
            body.AppendLine(
                CultureInfo.InvariantCulture,
                $"{indent}/// <param name=\"{parameter.Name.TrimStart('@')}\">{string.Join(" ", Identifiers.DocLines(parameter.Description))}</param>");
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"{indent}[{operation.Verb}({Identifiers.Literal(operation.Template)})]");

        foreach (var attribute in operation.Attributes)
            body.AppendLine(CultureInfo.InvariantCulture, $"{indent}{attribute}");

        if (operation.Deprecated)
            body.AppendLine(CultureInfo.InvariantCulture, $"{indent}[Obsolete(\"Deprecated in the OpenAPI document.\")]");

        body.AppendLine(CultureInfo.InvariantCulture, $"{indent}{operation.ReturnType} {operation.Name}(");

        for (var i = 0; i < operation.Parameters.Count; i++)
        {
            var separator = i == operation.Parameters.Count - 1 ? ");" : ",";
            body.AppendLine(CultureInfo.InvariantCulture, $"{indent}    {operation.Parameters[i].Declaration}{separator}");
        }
    }

    private static string WriteModels(string header, string ns, SchemaTypes types)
    {
        var body = new StringBuilder();
        var first = true;

        foreach (var model in types.Models)
        {
            if (!first)
                body.AppendLine();

            first = false;
            WriteModel(body, model, ns);
        }

        foreach (var constants in types.Constants)
        {
            if (!first)
                body.AppendLine();

            first = false;
            WriteConstants(body, constants);
        }

        // Schemas are documented where the document documents them and nowhere else; inventing a
        // summary per property would be noise. So CS1591 is off for this file only.
        body.Insert(0, "#pragma warning disable CS1591 // Not every schema in the document is described." + Environment.NewLine + Environment.NewLine);

        return Assemble(header, ns, body.ToString(), "System.Text.Json.Serialization",
            types.Models.Any(model => model.UsedAsQuery) ? "Runax.Http" : null);
    }

    private static void WriteModel(StringBuilder body, ModelDefinition model, string ns)
    {
        WriteSummary(body, string.Empty, model.Description);

        if (model.Deprecated)
            body.AppendLine("[Obsolete(\"Deprecated in the OpenAPI document.\")]");

        if (model.Properties.Count == 0)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"public sealed record {model.Name};");
            return;
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"public sealed record {model.Name}");
        body.AppendLine("{");

        for (var i = 0; i < model.Properties.Count; i++)
        {
            var property = model.Properties[i];

            if (i > 0)
                body.AppendLine();

            // Escaped here rather than when written, so the cref below survives as markup.
            var description = string.Join("\n", Identifiers.DocLines(property.Description));
            var doc = property.Type.Constants is { } constants
                // Qualified, because inside a record with a property of the same name — Book.Genre —
                // an unqualified cref would resolve to the property.
                ? Join(description, $"One of the values in <see cref=\"{ns}.{constants.Name}\"/>.")
                : property.Type.EnumValues is { Count: > 0 } values
                    ? Join(description, string.Join(" ", Identifiers.DocLines($"One of: {string.Join(", ", values)}.")))
                    : description;

            // Deprecation is documented rather than marked [Obsolete]: the JSON source generator
            // reads every property, and an obsolete one would warn inside generated code.
            if (property.Deprecated)
                doc = Join(doc, "Deprecated in the OpenAPI document.");

            WriteSummary(body, "    ", doc, escape: false);
            body.AppendLine(CultureInfo.InvariantCulture, $"    [JsonPropertyName({Identifiers.Literal(property.WireName)})]");

            // The generator names query keys from [Query], not from [JsonPropertyName], so a record
            // sent as a query object spells its keys out wherever the derived one would differ.
            if (model.UsedAsQuery && Identifiers.DerivedQueryKey(property.Name) != property.WireName)
                body.AppendLine(CultureInfo.InvariantCulture, $"    [Query({Identifiers.Literal(property.WireName)})]");

            var type = property.Nullable || !property.Required ? property.Type.Nullable() : property.Type.Name;
            var required = property.Required ? "required " : string.Empty;

            body.AppendLine(CultureInfo.InvariantCulture, $"    public {required}{type} {property.Name} {{ get; init; }}");
        }

        body.AppendLine("}");
    }

    private static void WriteConstants(StringBuilder body, ConstantsDefinition constants)
    {
        WriteSummary(body, string.Empty, Join(constants.Description, "The values the API accepts, as constants."));
        body.AppendLine(CultureInfo.InvariantCulture, $"public static class {constants.Name}");
        body.AppendLine("{");

        var names = new HashSet<string>(StringComparer.Ordinal) { constants.Name };

        foreach (var value in constants.Values)
            body.AppendLine(CultureInfo.InvariantCulture, $"    public const string {Identifiers.Unique(Identifiers.Pascal(value), names)} = {Identifiers.Literal(value)};");

        body.AppendLine("}");
    }

    private static string WriteContext(
        string header,
        string ns,
        string contextName,
        string interfaceName,
        List<string> serialized)
    {
        var body = new StringBuilder();

        body.AppendLine("/// <summary>");
        body.AppendLine(CultureInfo.InvariantCulture, $"/// Source-generated JSON metadata for every type <see cref=\"{interfaceName}\"/> sends or receives,");
        body.AppendLine("/// which keeps serialization reflection-free under trimming and native AOT.");
        body.AppendLine("/// </summary>");
        body.AppendLine("/// <remarks>");
        body.AppendLine(CultureInfo.InvariantCulture, $"/// Register it with <c>services.AddRunaxHttp().UseSystemTextJson({contextName}.Default.Options)</c>.");
        body.AppendLine("/// Null properties are left out of request bodies, so an optional field that was not set is");
        body.AppendLine("/// absent rather than sent as null — which an API reading PATCH bodies treats differently.");
        body.AppendLine("/// </remarks>");
        body.AppendLine("[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]");

        foreach (var type in serialized)
            body.AppendLine(CultureInfo.InvariantCulture, $"[JsonSerializable(typeof({type}))]");

        body.AppendLine(CultureInfo.InvariantCulture, $"public partial class {contextName} : JsonSerializerContext");
        body.AppendLine("{");
        body.AppendLine("}");

        return Assemble(header, ns, body.ToString(), "System.Text.Json.Serialization");
    }

    /// <summary>
    /// Puts a file together, with a using for each namespace its body actually needs — so the output
    /// compiles with or without implicit usings, and carries none that are unused.
    /// </summary>
    private static string Assemble(string header, string ns, string body, params string?[] always)
    {
        var usings = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var name in always)
        {
            if (name is not null)
                usings.Add(name);
        }

        void When(string pattern, string needed)
        {
            if (Regex.IsMatch(body, $@"\b{pattern}\b"))
                usings.Add(needed);
        }

        When("(Uri|Guid|DateTimeOffset|DateOnly|TimeOnly|Obsolete)", "System");
        When("(IReadOnlyList|IReadOnlyDictionary|IEnumerable)", "System.Collections.Generic");
        When("Stream", "System.IO");
        When("JsonElement", "System.Text.Json");
        When("(CancellationToken)", "System.Threading");
        When("Task", "System.Threading.Tasks");
        When("MultipartPart", "Runax.Http.Transports");

        var file = new StringBuilder();

        file.AppendLine(header);
        file.AppendLine();

        var system = usings.Where(name => name.StartsWith("System", StringComparison.Ordinal)).ToList();
        var other = usings.Except(system).ToList();

        foreach (var name in system)
            file.AppendLine(CultureInfo.InvariantCulture, $"using {name};");

        foreach (var name in other)
            file.AppendLine(CultureInfo.InvariantCulture, $"using {name};");

        file.AppendLine();
        file.AppendLine(CultureInfo.InvariantCulture, $"namespace {ns};");
        file.AppendLine();
        file.Append(body);

        return file.ToString().ReplaceLineEndings();
    }

    /// <summary>The first server's URL with its variables at their defaults, if it is absolute.</summary>
    /// <remarks>
    /// The scheme is checked, not just absoluteness: on Linux and macOS "/v1" parses as an absolute
    /// file:// URI, which is exactly the relative server this is meant to catch.
    /// </remarks>
    private static bool IsHttp(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https";

    private static string? BaseAddress(OpenApiDocument document, Uri? source, List<string> warnings, List<string> remarks)
    {
        if (document.Servers is not { Count: > 0 } servers || servers[0].Url is not { Length: > 0 } url)
        {
            warnings.Add("The document names no server, so there is no [BaseAddress]; set one with WithBaseAddress(...).");
            return null;
        }

        foreach (var (name, variable) in servers[0].Variables ?? new Dictionary<string, OpenApiServerVariable>())
            url = url.Replace("{" + name + "}", variable.Default ?? string.Empty, StringComparison.Ordinal);

        // A relative server is relative to wherever the document was served from, so a document
        // read from a URL can still be given its real address. One read from a file cannot.
        if (!IsHttp(url) && source is { Scheme: "http" or "https" } && Uri.TryCreate(source, url, out var resolved))
            url = resolved.ToString();

        if (servers.Count > 1)
        {
            remarks.Add("Other servers: " + string.Join(", ", servers.Skip(1).Select(server => server.Url)) + ".");
        }

        if (IsHttp(url))
            return url.TrimEnd('/');

        warnings.Add($"The server URL '{url}' is relative, so there is no [BaseAddress]; set one with WithBaseAddress(...).");
        remarks.Add(
            $"The document's server is the relative URL <c>{string.Concat(Identifiers.DocLines(url))}</c>; "
            + "set the address with <c>WithBaseAddress(...)</c>.");

        return null;
    }

    /// <summary>How to configure each security scheme the document declares.</summary>
    private static IEnumerable<string> Authentication(OpenApiDocument document)
    {
        foreach (var (name, scheme) in document.Components?.SecuritySchemes ?? new Dictionary<string, IOpenApiSecurityScheme>())
        {
            var label = Identifiers.DocLines(name).FirstOrDefault() ?? name;

            yield return scheme.Type switch
            {
                SecuritySchemeType.Http when string.Equals(scheme.Scheme, "basic", StringComparison.OrdinalIgnoreCase) =>
                    $"Authentication '{label}' is HTTP Basic: <c>UseBearerToken(provider, scheme: \"Basic\")</c> with the encoded credentials.",
                SecuritySchemeType.Http or SecuritySchemeType.OAuth2 or SecuritySchemeType.OpenIdConnect =>
                    $"Authentication '{label}' is a bearer token: <c>UseBearerToken(...)</c> when registering the client.",
                SecuritySchemeType.ApiKey when scheme.In == ParameterLocation.Header =>
                    $"Authentication '{label}' is an API key in the <c>{scheme.Name}</c> header: "
                    + $"<c>UseBearerToken(provider, headerName: \"{scheme.Name}\", scheme: null)</c>.",
                _ =>
                    $"Authentication '{label}' sends a key in the {scheme.In?.ToString().ToLowerInvariant()} as "
                    + $"<c>{scheme.Name}</c>, which needs adding by hand — a parameter, or a pipeline stage."
            };
        }
    }

    private static void WriteSummary(StringBuilder body, string indent, string? text, bool escape = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        body.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// <summary>");

        foreach (var line in escape ? Identifiers.DocLines(text) : text.Split('\n'))
            body.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// {line}");

        body.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// </summary>");
    }

    private static void WriteRemarks(StringBuilder body, string indent, List<string> lines)
    {
        if (lines.Count == 0)
            return;

        body.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// <remarks>");

        foreach (var line in lines)
            body.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// {line}");

        body.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// </remarks>");
    }

    private static string? Join(string? first, string? second)
        => (first, second) switch
        {
            (null or "", _) => second,
            (_, null or "") => first,
            _ => $"{first.TrimEnd().TrimEnd('.')}. {second}"
        };
}
