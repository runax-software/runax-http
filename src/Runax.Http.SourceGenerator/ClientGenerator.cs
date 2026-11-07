using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Runax.Http.SourceGenerator;

/// <summary>
/// Turns an interface annotated with request attributes into an implementation, at compile time.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ClientGenerator : IIncrementalGenerator
{
    private const string AttributesNamespace = "Runax.Http";
    private const string ContractMarkerAttribute = "Runax.Http.HttpClientContractAttribute";

    /// <summary>
    /// Fully qualified, and keeping nullable reference annotations. The stock
    /// <see cref="QualifiedWithNullability"/> drops them, which makes a generated
    /// method's parameters non-nullable where the interface declared them nullable — a
    /// CS8767 nullability mismatch on every optional reference parameter.
    /// </summary>
    private static readonly SymbolDisplayFormat QualifiedWithNullability =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly Regex PlaceholderPattern = new(
        @"\{(?<name>[^{}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Keyed off type declarations that could carry members: the predicate runs on every syntax
        // node in the compilation, so it has to be cheap and purely syntactic. Classes and structs
        // are included so that request attributes in the wrong place can be reported (RXH001)
        // rather than silently ignored.
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { Members.Count: > 0 }
                                    or TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, token) => Analyze(ctx, token))
            .Where(static result => result is not null);

        context.RegisterSourceOutput(candidates, static (ctx, result) =>
        {
            foreach (var diagnostic in result!.Diagnostics)
                ctx.ReportDiagnostic(diagnostic);

            if (result.Model is not null)
                ctx.AddSource(result.HintName!, Emitter.Emit(result.Model));
        });
    }

    private static AnalysisResult? Analyze(GeneratorSyntaxContext context, CancellationToken token)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, token) is not INamedTypeSymbol contract)
            return null;

        var methodSymbols = contract.GetMembers().OfType<IMethodSymbol>().ToList();
        var hasRequestAttribute = methodSymbols.Any(method => FindRequestAttribute(method) is not null);
        var isMarked = contract.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == ContractMarkerAttribute);

        // Only interfaces that actually describe requests are ours. Everything else in the
        // compilation has to pass through untouched and without a diagnostic.
        if (!hasRequestAttribute && !isMarked)
            return null;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (contract.TypeKind != TypeKind.Interface)
        {
            // Request attributes on a class or struct do nothing. Saying so beats ignoring them:
            // the author has written what looks like a client and would otherwise find out when
            // the methods they expected to be implemented throw NotImplementedException.
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.ContractMustBeInterface, Location(contract), contract.ToDisplayString()));

            return new AnalysisResult(null, null, diagnostics.ToImmutable());
        }

        if (contract.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.GenericContract, Location(contract), contract.ToDisplayString()));

            return new AnalysisResult(null, null, diagnostics.ToImmutable());
        }

        var baseAddress = ReadBaseAddress(contract, diagnostics);
        var transportName = ReadTransportName(contract);
        var defaultHeaders = ReadConstantHeaders(contract);
        var methods = ImmutableArray.CreateBuilder<MethodModel>();

        foreach (var member in contract.GetMembers())
        {
            token.ThrowIfCancellationRequested();

            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    {
                        var methodModel = AnalyzeMethod(method, diagnostics);
                        if (methodModel is not null)
                            methods.Add(methodModel);

                        break;
                    }

                // Property and event accessors arrive as methods too; the property or event itself
                // is what gets reported, so the accessors are skipped to avoid three diagnostics
                // for one declaration.
                case IMethodSymbol:
                    break;

                case IPropertySymbol or IEventSymbol:
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedMember,
                        Location(member),
                        member.ToDisplayString(),
                        member is IPropertySymbol ? "property" : "event"));

                    break;
            }
        }

        if (methods.Count == 0)
            return new AnalysisResult(null, null, diagnostics.ToImmutable());

        var declaredTransports = ImmutableArray.CreateBuilder<string>();
        var seenTransports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in methods.Select(method => method.TransportName).Prepend(transportName))
        {
            if (name is not null && seenTransports.Add(name))
                declaredTransports.Add(name);
        }

        var model = new ClientModel(
            contract.Name,
            contract.ToDisplayString(QualifiedWithNullability),
            contract.ContainingNamespace.IsGlobalNamespace
                ? null
                : contract.ContainingNamespace.ToDisplayString(),
            BuildImplementationName(contract.Name),
            baseAddress,
            transportName,
            defaultHeaders,
            declaredTransports.ToImmutable(),
            methods.ToImmutable());

        return new AnalysisResult(model, $"{contract.Name}.RunaxClient.g.cs", diagnostics.ToImmutable());
    }

    private static MethodModel? AnalyzeMethod(
        IMethodSymbol method,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var requestAttribute = FindRequestAttribute(method);

        if (requestAttribute is null)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.MissingRequestAttribute, Location(method), method.ToDisplayString()));

            return null;
        }

        if (method.IsGenericMethod)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.GenericMethod, Location(method), method.ToDisplayString()));

            return null;
        }

        var verb = requestAttribute.AttributeClass!.Name.Replace("Attribute", string.Empty);
        var template = requestAttribute.ConstructorArguments.Length > 0
            ? requestAttribute.ConstructorArguments[0].Value as string ?? string.Empty
            : string.Empty;

        if (!TryResolveResultShape(method, out var shape, out var contentTypeName))
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.UnsupportedReturnType,
                Location(method),
                method.ToDisplayString(),
                method.ReturnType.ToDisplayString()));

            return null;
        }

        var responses = ReadResponses(method, diagnostics);

        if (responses.Length > 0 && shape != ResultShape.ApiResult)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.ResponseNeedsApiResult,
                Location(method),
                method.ToDisplayString(),
                method.ReturnType.ToDisplayString()));

            return null;
        }

        if (responses.Length == 0 && shape == ResultShape.ApiResult)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.ApiResultNeedsResponse, Location(method), method.ToDisplayString()));

            return null;
        }

        var parameters = AnalyzeParameters(method, template, diagnostics, out var bodyFormat);

        ValidatePlaceholders(method, template, parameters, diagnostics);

        if (bodyFormat is not null && verb is "Get" or "Head" or "Options")
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.BodyOnBodylessMethod,
                Location(method),
                method.ToDisplayString(),
                verb.ToUpperInvariant()));
        }

        return new MethodModel(
            method.Name,
            verb,
            template,
            method.ReturnType.ToDisplayString(QualifiedWithNullability),
            shape,
            contentTypeName,
            ReadTransportName(method),
            parameters,
            ReadConstantHeaders(method),
            responses,
            bodyFormat ?? "Json");
    }

    private static ImmutableArray<ParameterModel> AnalyzeParameters(
        IMethodSymbol method,
        string template,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        out string? bodyFormat)
    {
        var placeholders = PlaceholderNames(template);
        var parameters = ImmutableArray.CreateBuilder<ParameterModel>();
        var bodyCount = 0;
        var tokenCount = 0;

        bodyFormat = null;

        foreach (var parameter in method.Parameters)
        {
            var typeName = parameter.Type.ToDisplayString(QualifiedWithNullability);

            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                if (++tokenCount > 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MultipleCancellationTokens, Location(method), method.ToDisplayString()));

                    continue;
                }

                parameters.Add(new ParameterModel(
                    parameter.Name, parameter.Name, ParameterBinding.CancellationToken, typeName));

                continue;
            }

            var attribute = FindBindingAttribute(parameter, out var binding);

            if (attribute is null)
            {
                // An unannotated parameter binds to a placeholder of the same name, because
                // repeating the name in a [Path] attribute is noise. Anything else is an error:
                // guessing that it must be a query parameter is how a secret ends up in a URL.
                if (placeholders.Contains(parameter.Name))
                {
                    parameters.Add(new ParameterModel(
                        parameter.Name, parameter.Name, ParameterBinding.Path, typeName));

                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.UnboundParameter,
                    Location(parameter),
                    parameter.Name,
                    method.ToDisplayString()));

                continue;
            }

            if (binding == ParameterBinding.Body)
            {
                if (++bodyCount > 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.MultipleBodyParameters, Location(method), method.ToDisplayString()));

                    continue;
                }

                bodyFormat = ReadBodyFormat(attribute);

                // Form and multipart bodies are built from the argument's own shape rather than by
                // reflecting over it, so the type has to be one the runtime can enumerate. Caught
                // here, because the alternative is a confusing compile error inside generated code.
                if (DescribeBodyTypeProblem(parameter.Type, bodyFormat) is { } problem)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedBodyParameterType,
                        Location(parameter),
                        parameter.Name,
                        method.ToDisplayString(),
                        parameter.Type.ToDisplayString(),
                        bodyFormat,
                        problem));

                    continue;
                }
            }

            parameters.Add(new ParameterModel(
                parameter.Name,
                ReadWireName(attribute) ?? parameter.Name,
                binding,
                typeName));
        }

        return parameters.ToImmutable();
    }

    private static void ValidatePlaceholders(
        IMethodSymbol method,
        string template,
        ImmutableArray<ParameterModel> parameters,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var bound = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (parameter.Binding != ParameterBinding.Path)
                continue;

            bound.TryGetValue(parameter.WireName, out var count);
            bound[parameter.WireName] = count + 1;
        }

        foreach (var placeholder in PlaceholderNames(template))
        {
            if (!bound.TryGetValue(placeholder, out var count))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.UnmatchedPathPlaceholder,
                    Location(method),
                    template,
                    placeholder,
                    method.ToDisplayString()));

                continue;
            }

            if (count > 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.DuplicatePathBinding,
                    Location(method),
                    method.ToDisplayString(),
                    placeholder));
            }
        }
    }

    private static bool TryResolveResultShape(
        IMethodSymbol method,
        out ResultShape shape,
        out string? contentTypeName)
    {
        shape = ResultShape.Empty;
        contentTypeName = null;

        if (method.ReturnType is not INamedTypeSymbol returnType)
            return false;

        var unbound = returnType.ToDisplayString(QualifiedWithNullability);

        if (unbound == "global::System.Threading.Tasks.Task")
        {
            shape = ResultShape.Empty;
            return true;
        }

        if (returnType is not { IsGenericType: true, Arity: 1 }
            || returnType.ConstructedFrom.ToDisplayString() != "System.Threading.Tasks.Task<TResult>")
        {
            return false;
        }

        var result = returnType.TypeArguments[0];
        var resultName = result.ToDisplayString();

        if (resultName == "Runax.Http.ApiResponse")
        {
            shape = ResultShape.ApiResponse;
            return true;
        }

        if (resultName == "Runax.Http.ApiResult")
        {
            shape = ResultShape.ApiResult;
            return true;
        }

        if (result is INamedTypeSymbol { IsGenericType: true, Arity: 1 } apiResponse
            && apiResponse.ConstructedFrom.ToDisplayString() == "Runax.Http.ApiResponse<T>")
        {
            shape = ResultShape.ApiResponseOfContent;
            contentTypeName = apiResponse.TypeArguments[0]
                .ToDisplayString(QualifiedWithNullability);

            return true;
        }

        shape = ResultShape.Content;
        contentTypeName = result.ToDisplayString(QualifiedWithNullability);

        return true;
    }

    /// <summary>
    /// Reads the <c>[Response]</c> declarations in source order, since the first match wins.
    /// </summary>
    private static ImmutableArray<ResponseModel> ReadResponses(
        IMethodSymbol method,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var responses = ImmutableArray.CreateBuilder<ResponseModel>();
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        var sawUnconditional = false;

        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != $"{AttributesNamespace}.ResponseAttribute")
                continue;

            var arguments = attribute.ConstructorArguments;

            // Two constructors: (Type) for the fallback, (int, Type) for a status code.
            var statusCode = arguments.Length == 2 ? arguments[0].Value as int? : null;
            var typeSymbol = (arguments.Length == 2 ? arguments[1].Value : arguments[0].Value) as ITypeSymbol;

            if (typeSymbol is null)
                continue;

            int? min = null, max = null;
            string? jsonProperty = null, jsonEquals = null, header = null, headerEquals = null;

            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "MinStatusCode" when named.Value.Value is int low and > 0:
                        min = low;
                        break;
                    case "MaxStatusCode" when named.Value.Value is int high and > 0:
                        max = high;
                        break;
                    case "WhenJsonProperty":
                        jsonProperty = named.Value.Value as string;
                        break;
                    case "WhenJsonPropertyEquals":
                        jsonEquals = named.Value.Value as string;
                        break;
                    case "WhenHeader":
                        header = named.Value.Value as string;
                        break;
                    case "WhenHeaderEquals":
                        headerEquals = named.Value.Value as string;
                        break;
                }
            }

            var response = new ResponseModel(
                typeSymbol.ToDisplayString(QualifiedWithNullability),
                statusCode,
                min,
                max,
                jsonProperty,
                jsonEquals,
                header,
                headerEquals);

            if (sawUnconditional)
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.UnreachableResponse, Location(method), method.ToDisplayString()));

                continue;
            }

            // Two responses with identical conditions mean the second never runs, and which type a
            // response became would come down to declaration order.
            var key = Describe(response);

            if (!conditions.Add(key))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.DuplicateResponseCondition,
                    Location(method),
                    method.ToDisplayString(),
                    key));

                continue;
            }

            sawUnconditional |= response.IsUnconditional;
            responses.Add(response);
        }

        return responses.ToImmutable();
    }

    /// <summary>A response's conditions as text, for duplicate detection and diagnostics.</summary>
    private static string Describe(ResponseModel response)
    {
        if (response.IsUnconditional)
            return "any response";

        var parts = new List<string>();

        if (response.StatusCode is { } status)
            parts.Add($"status {status.ToString(CultureInfo.InvariantCulture)}");

        if (response.MinStatusCode is not null || response.MaxStatusCode is not null)
        {
            var low = response.MinStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "*";
            var high = response.MaxStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "*";

            parts.Add($"status {low}-{high}");
        }

        if (response.JsonProperty is { } property)
        {
            parts.Add(response.JsonPropertyEquals is { } expected
                ? $"body property '{property}' == '{expected}'"
                : $"body property '{property}'");
        }

        if (response.Header is { } headerName)
        {
            parts.Add(response.HeaderEquals is { } headerValue
                ? $"header '{headerName}' == '{headerValue}'"
                : $"header '{headerName}'");
        }

        return string.Join(" and ", parts);
    }

    private static AttributeData? FindRequestAttribute(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            for (var type = attribute.AttributeClass?.BaseType; type is not null; type = type.BaseType)
            {
                if (type.ToDisplayString() == $"{AttributesNamespace}.HttpMethodAttribute")
                    return attribute;
            }
        }

        return null;
    }

    private static AttributeData? FindBindingAttribute(IParameterSymbol parameter, out ParameterBinding binding)
    {
        binding = default;

        foreach (var attribute in parameter.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case $"{AttributesNamespace}.PathAttribute":
                    binding = ParameterBinding.Path;
                    return attribute;

                case $"{AttributesNamespace}.QueryAttribute":
                    binding = ParameterBinding.Query;
                    return attribute;

                case $"{AttributesNamespace}.BodyAttribute":
                    binding = ParameterBinding.Body;
                    return attribute;

                case $"{AttributesNamespace}.HeaderAttribute":
                    binding = ParameterBinding.Header;
                    return attribute;
            }
        }

        return null;
    }

    private static string? ReadWireName(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length > 0
            && attribute.ConstructorArguments[0].Value is string name
            && !string.IsNullOrEmpty(name))
        {
            return name;
        }

        return null;
    }

    /// <summary>
    /// Whether this type can be sent in the given body format, and if not, what to do instead.
    /// </summary>
    /// <returns>The advice to include in the diagnostic, or <see langword="null"/> if it is fine.</returns>
    private static string? DescribeBodyTypeProblem(ITypeSymbol type, string bodyFormat)
    {
        switch (bodyFormat)
        {
            case "UrlEncoded":
                foreach (var element in EnumerableElementTypes(type))
                {
                    if (element is INamedTypeSymbol { Arity: 2 } pair
                        && pair.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.KeyValuePair<TKey, TValue>"
                        && pair.TypeArguments[0].SpecialType == SpecialType.System_String)
                    {
                        return null;
                    }
                }

                return "A form body is a sequence of key/value pairs — a Dictionary<string, string> "
                       + "or an IEnumerable<KeyValuePair<string, object?>>, say. Mapping an object's "
                       + "properties to fields would mean reflecting over it at run time, which this "
                       + "library does not do; use BodyFormat.Json, or project it into a dictionary "
                       + "where the names are visible.";

            case "Multipart":
                foreach (var element in EnumerableElementTypes(type))
                {
                    if (element.ToDisplayString() == "Runax.Http.Transports.MultipartPart")
                        return null;
                }

                return "A multipart body is a sequence of MultipartPart, built at the call site. "
                       + "There is no mapping from an object's properties to parts, because that "
                       + "would mean reflecting over it at run time.";

            case "Raw":
                var raw = type.ToDisplayString();

                if (raw is "string" or "byte[]" or "System.IO.Stream"
                    or "System.ReadOnlyMemory<byte>"
                    || type.AllInterfaces.Any(i => i.ToDisplayString() == "System.IO.Stream")
                    || InheritsFromStream(type))
                {
                    return null;
                }

                return "A raw body is sent as written, so it has to be a string, byte[], "
                       + "ReadOnlyMemory<byte> or Stream.";

            default:
                return null;
        }
    }

    /// <summary>The element types of every <c>IEnumerable&lt;T&gt;</c> this type implements.</summary>
    private static IEnumerable<ITypeSymbol> EnumerableElementTypes(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { Arity: 1 } self
            && self.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
        {
            yield return self.TypeArguments[0];
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (implemented is { Arity: 1 }
                && implemented.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            {
                yield return implemented.TypeArguments[0];
            }
        }
    }

    private static bool InheritsFromStream(ITypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.IO.Stream")
                return true;
        }

        return false;
    }

    private static string ReadBodyFormat(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Format" && argument.Value.Value is int value)
            {
                return value switch
                {
                    1 => "UrlEncoded",
                    2 => "Multipart",
                    3 => "Raw",
                    _ => "Json"
                };
            }
        }

        return "Json";
    }

    private static string? ReadBaseAddress(
        INamedTypeSymbol contract,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var attribute in contract.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != $"{AttributesNamespace}.BaseAddressAttribute")
                continue;

            var value = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string
                : null;

            if (string.IsNullOrWhiteSpace(value)
                || !Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.InvalidBaseAddress,
                    Location(contract),
                    value ?? "<null>",
                    contract.ToDisplayString()));

                return null;
            }

            return value;
        }

        return null;
    }

    private static string? ReadTransportName(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == $"{AttributesNamespace}.TransportAttribute"
                && attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string name)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the headers declared with both a name and a value, which are the ones that can be
    /// baked in. A single-argument <c>[Header]</c> on a parameter names a header whose value comes
    /// from the argument, and is handled as a parameter binding instead.
    /// </summary>
    private static ImmutableArray<KeyValuePair<string, string>> ReadConstantHeaders(ISymbol symbol)
    {
        var headers = ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != $"{AttributesNamespace}.HeaderAttribute"
                || attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is string name
                && attribute.ConstructorArguments[1].Value is string value)
            {
                headers.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        return headers.ToImmutable();
    }

    private static HashSet<string> PlaceholderNames(string template)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in PlaceholderPattern.Matches(template))
            names.Add(match.Groups["name"].Value);

        return names;
    }

    /// <summary>
    /// Derives the generated class's name from the interface: <c>IRepositoryClient</c> becomes
    /// <c>RepositoryClientRunaxClient</c>. The suffix is there so the name cannot collide with a
    /// hand-written type that happens to be the interface minus its I.
    /// </summary>
    private static string BuildImplementationName(string contractName)
    {
        var stem = contractName.Length > 1 && contractName[0] == 'I' && char.IsUpper(contractName[1])
            ? contractName.Substring(1)
            : contractName;

        return $"{stem}RunaxClient";
    }

    private static Location Location(ISymbol symbol)
        => symbol.Locations.FirstOrDefault() ?? Microsoft.CodeAnalysis.Location.None;

    /// <summary>What one interface produced: a model to emit, diagnostics, or both.</summary>
    private sealed class AnalysisResult
    {
        internal AnalysisResult(ClientModel? model, string? hintName, ImmutableArray<Diagnostic> diagnostics)
        {
            Model = model;
            HintName = hintName;
            Diagnostics = diagnostics;
        }

        internal ClientModel? Model { get; }

        internal string? HintName { get; }

        internal ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
