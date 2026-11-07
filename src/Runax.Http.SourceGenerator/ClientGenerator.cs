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

        // JSON context coverage is a second, compilation-wide pass: it needs every contract's
        // serialized types joined against every JsonSerializerContext, which no per-interface
        // analysis can see. See JsonContextCoverage for why this is a diagnostic and not an
        // emitted context.
        var contexts = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => JsonContextCoverage.CouldBeJsonContext(node),
                static (ctx, token) => JsonContextCoverage.ReadContext(ctx, token))
            .Where(static found => found is not null);

        context.RegisterSourceOutput(
            candidates.Collect().Combine(contexts.Collect()),
            static (ctx, pair) =>
            {
                var serialized = pair.Left
                    .SelectMany(static result => result!.SerializedTypes)
                    .ToImmutableArray();

                var declared = pair.Right
                    .Select(static found => found!)
                    .ToImmutableArray();

                foreach (var diagnostic in JsonContextCoverage.Check(serialized, declared))
                    ctx.ReportDiagnostic(diagnostic);
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

        var queryNaming = ReadQueryNaming(contract);
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
                        var methodModel = AnalyzeMethod(method, queryNaming, diagnostics);
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

        return new AnalysisResult(
            model,
            $"{contract.Name}.RunaxClient.g.cs",
            diagnostics.ToImmutable(),
            JsonContextCoverage.Collect(model, Location(contract)));
    }

    private static MethodModel? AnalyzeMethod(
        IMethodSymbol method,
        string queryNaming,
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

        var parameters = AnalyzeParameters(method, queryNaming, template, diagnostics, out var bodyFormat);

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
            ReadRetry(method, verb, diagnostics),
            bodyFormat ?? "Json");
    }

    private static ImmutableArray<ParameterModel> AnalyzeParameters(
        IMethodSymbol method,
        string queryNaming,
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

            // A policy applies to the derived name only; an explicit [Query("name")] is the
            // author stating the key outright and is never transformed.
            var effectiveNaming = binding == ParameterBinding.Query
                ? ReadParameterQueryNaming(attribute) ?? queryNaming
                : QueryNameFormatter.AsDeclared;

            var wireName = ReadWireName(attribute)
                           ?? (binding == ParameterBinding.Query
                               ? QueryNameFormatter.Apply(parameter.Name, effectiveNaming)
                               : parameter.Name);

            if (binding == ParameterBinding.Query)
            {
                // A query value is formatted, never serialized, so the runtime can only turn it
                // into text if the type is one it recognises. Left unchecked, an unrecognised type
                // falls through to ToString() and the server receives "Filter { State = open }" —
                // garbage, with no warning anywhere.
                var shape = ClassifyQuery(parameter.Type, out var fields, out var problem);

                if (problem is not null)
                {
                    diagnostics.Add(problem.Property is null
                        ? Diagnostic.Create(
                            Diagnostics.UnsupportedQueryParameterType,
                            Location(parameter),
                            parameter.Name,
                            method.ToDisplayString(),
                            parameter.Type.ToDisplayString(),
                            problem.Advice)
                        : Diagnostic.Create(
                            Diagnostics.UnsupportedQueryObjectProperty,
                            Location(parameter),
                            parameter.Name,
                            method.ToDisplayString(),
                            problem.Property,
                            problem.PropertyType,
                            problem.Advice));

                    continue;
                }

                parameters.Add(new ParameterModel(
                    parameter.Name,
                    wireName,
                    binding,
                    typeName,
                    shape,
                    BuildQueryFields(parameter, fields, effectiveNaming)));

                continue;
            }

            parameters.Add(new ParameterModel(parameter.Name, wireName, binding, typeName));
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

    /// <summary>
    /// Reads <c>[Retry]</c> from a method, reporting the two ways it can be written uselessly.
    /// </summary>
    private static RetryModel? ReadRetry(
        IMethodSymbol method,
        string verb,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != $"{AttributesNamespace}.RetryAttribute")
                continue;

            var maxAttempts = attribute.ConstructorArguments.Length > 0
                              && attribute.ConstructorArguments[0].Value is int declared
                ? declared
                : 1;

            var delayMilliseconds = -1;
            var allowNonIdempotent = false;

            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "DelayMilliseconds" when named.Value.Value is int delay:
                        delayMilliseconds = delay;
                        break;

                    case "AllowNonIdempotent" when named.Value.Value is bool allow:
                        allowNonIdempotent = allow;
                        break;
                }
            }

            if (maxAttempts < 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.InvalidRetryAttemptCount,
                    Location(method),
                    method.ToDisplayString(),
                    maxAttempts));

                return null;
            }

            // Asking for retries on a POST and silently getting none is the failure this warns
            // about: the safety default would leave the attribute doing nothing at all, and
            // nothing else in the build would mention it.
            if (maxAttempts > 1 && !allowNonIdempotent && verb is "Post" or "Patch")
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.RetryOnNonIdempotentMethod,
                    Location(method),
                    method.ToDisplayString(),
                    verb.ToUpperInvariant(),
                    maxAttempts));
            }

            return new RetryModel(maxAttempts, delayMilliseconds, allowNonIdempotent);
        }

        return null;
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

    /// <summary>Why a query parameter cannot be sent, and what to do about it.</summary>
    private sealed class QueryProblem
    {
        internal QueryProblem(string advice, string? property = null, string? propertyType = null)
        {
            Advice = advice;
            Property = property;
            PropertyType = propertyType;
        }

        internal string Advice { get; }

        /// <summary>The offending property, when the parameter itself was fine.</summary>
        internal string? Property { get; }

        internal string? PropertyType { get; }
    }

    private const string QueryAdvice =
        "A query value has to be something the runtime can turn into text: a string, number, bool, "
        + "enum, date, Guid or Uri, anything IFormattable, a collection of those, a dictionary keyed "
        + "by string, or an object whose properties are all of those.";

    /// <summary>
    /// Decides how a <c>[Query]</c> parameter becomes query keys.
    /// </summary>
    /// <remarks>
    /// Order matters. A string is an <c>IEnumerable&lt;char&gt;</c> and a dictionary is an
    /// <c>IEnumerable&lt;KeyValuePair&lt;,&gt;&gt;</c>, so the narrow cases are tested before the
    /// general one or both would be mis-classified as collections.
    /// </remarks>
    private static QueryShape ClassifyQuery(
        ITypeSymbol type,
        out ImmutableArray<IPropertySymbol> fields,
        out QueryProblem? problem)
    {
        fields = ImmutableArray<IPropertySymbol>.Empty;
        problem = null;

        if (IsSendableValue(type))
            return QueryShape.Scalar;

        if (IsStringKeyedSequence(type))
            return QueryShape.Dictionary;

        if (TryGetSequenceElement(type, out var element))
        {
            if (IsSendableValue(element!))
                return QueryShape.Scalar;

            problem = new QueryProblem(
                $"Its items are '{element!.ToDisplayString()}', which cannot be sent. " + QueryAdvice);

            return QueryShape.Scalar;
        }

        var properties = ReadableProperties(type);

        if (properties.Length == 0)
        {
            problem = new QueryProblem(QueryAdvice);
            return QueryShape.Scalar;
        }

        foreach (var property in properties)
        {
            if (IsSendableValue(property.Type))
                continue;

            if (TryGetSequenceElement(property.Type, out var itemType) && IsSendableValue(itemType!))
                continue;

            // Nesting is refused rather than flattened. There is no single convention for the key
            // of a nested field — ASP.NET Core spells it "filter.state", OpenAPI's deepObject
            // spells it "filter[state]", and others differ again — so picking one silently would be
            // the same guess this diagnostic exists to prevent. One level covers the flat filter
            // and pagination objects that motivate the feature.
            problem = new QueryProblem(
                ReadableProperties(property.Type).Length > 0
                    ? "A query object's properties cannot themselves be objects: there is no one "
                      + "convention for naming a nested field, so flattening would have to guess. "
                      + "Flatten the type, or pass the inner object as its own [Query] parameter."
                    : QueryAdvice,
                property.Name,
                property.Type.ToDisplayString());

            return QueryShape.Object;
        }

        fields = properties;

        return QueryShape.Object;
    }

    /// <summary>
    /// The public, readable, non-indexer instance properties of a type and its bases.
    /// </summary>
    /// <remarks>
    /// Inherited properties are included — a filter type with a shared pagination base is the
    /// obvious shape, and ignoring the base would drop fields silently. Declaration order is
    /// preserved, most-derived first, so the generated query string is stable across builds.
    /// </remarks>
    private static ImmutableArray<IPropertySymbol> ReadableProperties(ITypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface))
            return ImmutableArray<IPropertySymbol>.Empty;

        var properties = ImmutableArray.CreateBuilder<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = Unwrap(type); current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
                break;

            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.DeclaredAccessibility != Accessibility.Public
                    || member.IsStatic
                    || member.IsIndexer
                    || member.GetMethod is null
                    || !seen.Add(member.Name))
                {
                    continue;
                }

                properties.Add(member);
            }
        }

        return properties.ToImmutable();
    }

    /// <summary>Whether <c>RequestValueFormatter</c> can turn this type into text.</summary>
    /// <remarks>Mirrors that formatter's switch; the two have to agree or this check is a lie.</remarks>
    private static bool IsSendableValue(ITypeSymbol type)
    {
        var bare = StripNullable(type);

        if (bare.TypeKind == TypeKind.Enum)
            return true;

        switch (bare.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_DateTime:
                return true;
        }

        // Uri is handled explicitly by the formatter and is neither a special type nor
        // IFormattable, so it has to be named here too.
        if (bare.ToDisplayString() is "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly"
            or "System.TimeSpan" or "System.Guid" or "System.Uri")
        {
            return true;
        }

        return bare.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable");
    }

    /// <summary>Whether this is a sequence of key/value pairs keyed by string.</summary>
    private static bool IsStringKeyedSequence(ITypeSymbol type)
    {
        if (!TryGetSequenceElement(type, out var element))
            return false;

        return element is INamedTypeSymbol { Arity: 2 } pair
               && pair.ConstructedFrom.ToDisplayString()
                   == "System.Collections.Generic.KeyValuePair<TKey, TValue>"
               && pair.TypeArguments[0].SpecialType == SpecialType.System_String;
    }

    /// <summary>The element type of an <c>IEnumerable&lt;T&gt;</c>, excluding string.</summary>
    private static bool TryGetSequenceElement(ITypeSymbol type, out ITypeSymbol? element)
    {
        element = null;

        if (type.SpecialType == SpecialType.System_String)
            return false;

        var bare = StripNullable(type);

        if (bare is INamedTypeSymbol { Arity: 1 } self
            && self.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
        {
            element = self.TypeArguments[0];
            return true;
        }

        foreach (var implemented in bare.AllInterfaces)
        {
            if (implemented is { Arity: 1 }
                && implemented.ConstructedFrom.ToDisplayString()
                    == "System.Collections.Generic.IEnumerable<T>")
            {
                element = implemented.TypeArguments[0];
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol StripNullable(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static INamedTypeSymbol? Unwrap(ITypeSymbol type)
        => StripNullable(type) as INamedTypeSymbol;

    /// <summary>
    /// The access expression for each field of an expanded query object.
    /// </summary>
    /// <remarks>
    /// Null-conditional unless the parameter is a non-nullable struct, where it would not compile
    /// and a null object is impossible anyway. Using <c>?.</c> is also what makes a null object
    /// contribute nothing: every field reads as null, and a null query value is already omitted.
    /// </remarks>
    private static ImmutableArray<QueryFieldModel> BuildQueryFields(
        IParameterSymbol parameter,
        ImmutableArray<IPropertySymbol> properties,
        string queryNaming)
    {
        if (properties.IsDefaultOrEmpty)
            return ImmutableArray<QueryFieldModel>.Empty;

        var nullable = parameter.Type.IsReferenceType
                       || parameter.Type is INamedTypeSymbol
                       {
                           OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                       };

        var accessor = nullable ? "?." : ".";
        var fields = ImmutableArray.CreateBuilder<QueryFieldModel>(properties.Length);

        foreach (var property in properties)
        {
            fields.Add(new QueryFieldModel(
                ReadPropertyWireName(property)
                ?? QueryNameFormatter.Apply(property.Name, queryNaming),
                $"{parameter.Name}{accessor}{property.Name}"));
        }

        return fields.ToImmutable();
    }

    /// <summary>
    /// The naming convention an interface declares with <c>[QueryNaming]</c>, or camelCase.
    /// </summary>
    private static string ReadQueryNaming(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == $"{AttributesNamespace}.QueryNamingAttribute"
                && attribute.ConstructorArguments.Length == 1)
            {
                return NamingMemberName(attribute.ConstructorArguments[0].Value);
            }
        }

        return QueryNameFormatter.CamelCase;
    }

    /// <summary>
    /// The convention a <c>[Query(Naming = …)]</c> names, or <see langword="null"/> when it does
    /// not name one and the interface's policy should apply.
    /// </summary>
    private static string? ReadParameterQueryNaming(AttributeData attribute)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == "Naming")
                return NamingMemberName(named.Value.Value);
        }

        return null;
    }

    /// <summary>
    /// The <c>QueryNaming</c> member name for an attribute argument, which arrives as its
    /// underlying integer.
    /// </summary>
    private static string NamingMemberName(object? value) => value switch
    {
        1 => QueryNameFormatter.AsDeclared,
        2 => QueryNameFormatter.SnakeCaseLower,
        3 => QueryNameFormatter.SnakeCaseUpper,
        4 => QueryNameFormatter.KebabCaseLower,
        5 => QueryNameFormatter.KebabCaseUpper,
        _ => QueryNameFormatter.CamelCase
    };

    /// <summary>A property's <c>[Query("name")]</c> rename, if it carries one.</summary>
    private static string? ReadPropertyWireName(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == $"{AttributesNamespace}.QueryAttribute")
                return ReadWireName(attribute);
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
        internal AnalysisResult(
            ClientModel? model,
            string? hintName,
            ImmutableArray<Diagnostic> diagnostics,
            ImmutableArray<SerializedTypeRef> serializedTypes = default)
        {
            Model = model;
            HintName = hintName;
            Diagnostics = diagnostics;
            SerializedTypes = serializedTypes.IsDefault
                ? ImmutableArray<SerializedTypeRef>.Empty
                : serializedTypes;
        }

        internal ClientModel? Model { get; }

        internal string? HintName { get; }

        internal ImmutableArray<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// What this contract hands to the serializer, for the compilation-wide JSON context
        /// check. Empty for a result that produced no model, so the early returns above need no
        /// change.
        /// </summary>
        internal ImmutableArray<SerializedTypeRef> SerializedTypes { get; }
    }
}
