using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Runax.Http.SourceGenerator;

/// <summary>One type a contract hands to the serializer, and where it was declared.</summary>
internal sealed class SerializedTypeRef
{
    internal SerializedTypeRef(string typeName, string memberDisplay, Location location)
    {
        TypeName = typeName;
        MemberDisplay = memberDisplay;
        Location = location;
    }

    /// <summary>
    /// The fully qualified name, normalized so it can be compared with a <c>typeof()</c> argument.
    /// </summary>
    internal string TypeName { get; }

    /// <summary>The method that sends or receives it, for the diagnostic message.</summary>
    internal string MemberDisplay { get; }

    /// <summary>Where to report, so the warning lands on the method rather than the project.</summary>
    internal Location Location { get; }
}

/// <summary>A <c>JsonSerializerContext</c> in the compilation and what it registers.</summary>
internal sealed class JsonContextRef
{
    internal JsonContextRef(string contextName, ImmutableArray<string> registeredTypes)
    {
        ContextName = contextName;
        RegisteredTypes = registeredTypes;
    }

    internal string ContextName { get; }

    internal ImmutableArray<string> RegisteredTypes { get; }
}

/// <summary>
/// Checks that every type a client sends or receives is registered with a
/// <c>JsonSerializerContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a diagnostic rather than the context itself, and the reason is a hard Roslyn
/// constraint that is worth recording. Output registered with
/// <c>RegisterPostInitializationOutput</c> joins the compilation that every generator sees, so a
/// <c>JsonSerializerContext</c> emitted that way <em>is</em> picked up and populated by
/// System.Text.Json's own generator — verified. Output registered with
/// <c>RegisterSourceOutput</c> is not: the other generator never sees it, and the emitted partial
/// fails with <c>CS0534</c> for the abstract members nobody implemented.
/// </para>
/// <para>
/// The type set here can only come from analysing the contracts, which means
/// <c>RegisterSourceOutput</c>. So emitting the context is not merely awkward, it cannot work —
/// and the achievable thing is to stop the user forgetting a type, which was the actual pain.
/// </para>
/// </remarks>
internal static class JsonContextCoverage
{
    /// <summary>
    /// Types that ride along for free when any registered type has a property of that type, which
    /// in a real context is close enough to always that reporting them would be noise.
    /// </summary>
    /// <remarks>
    /// Established by probe rather than assumed: a context registering only a record with an
    /// <c>int</c> and a <c>string</c> property resolved <c>int</c> and <c>string</c> but returned
    /// null for <c>bool</c> and for every collection. So nothing is unconditionally free — these
    /// are merely overwhelmingly likely to be, and a method whose body or response is a bare
    /// primitive is rare enough that silence beats a false positive.
    /// </remarks>
    private static readonly ImmutableHashSet<string> RidesAlong = ImmutableHashSet.Create(
        "string", "object", "bool", "byte", "sbyte", "short", "ushort",
        "int", "uint", "long", "ulong", "float", "double", "decimal", "char");

    /// <summary>
    /// Everything a contract hands to the serializer.
    /// </summary>
    /// <param name="model">The analysed contract.</param>
    /// <param name="location">Where to report, which is the interface declaration.</param>
    /// <returns>One entry per type per method.</returns>
    /// <remarks>
    /// Read off the model's own type names rather than re-walking symbols, so there is exactly one
    /// understanding in the generator of what each return shape serializes. A second walk here
    /// would drift the first time a shape is added.
    /// </remarks>
    internal static ImmutableArray<SerializedTypeRef> Collect(ClientModel model, Location location)
    {
        var found = ImmutableArray.CreateBuilder<SerializedTypeRef>();

        foreach (var method in model.Methods)
        {
            var member = $"{model.ContractName}.{method.Name}";

            // Task<T> and Task<ApiResponse<T>> both deserialize T. Task, Task<ApiResponse> and
            // Task<ApiResult> carry no single type — the ApiResult types come from [Response].
            if (method.Shape is ResultShape.Content or ResultShape.ApiResponseOfContent
                && method.ContentTypeName is { } contentType)
            {
                found.Add(new SerializedTypeRef(Normalize(contentType), member, location));
            }

            foreach (var response in method.Responses)
                found.Add(new SerializedTypeRef(Normalize(response.TypeName), member, location));

            // Only a JSON body reaches the serializer. Raw is already bytes, and form and
            // multipart bodies are built from the argument's shape.
            if (method.BodyFormat != "Json")
                continue;

            foreach (var parameter in method.Parameters)
            {
                if (parameter.Binding == ParameterBinding.Body)
                    found.Add(new SerializedTypeRef(Normalize(parameter.TypeName), member, location));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// Finds the <c>JsonSerializerContext</c> classes in the compilation and what each registers.
    /// </summary>
    internal static JsonContextRef? ReadContext(GeneratorSyntaxContext context, CancellationToken token)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, token) is not INamedTypeSymbol type)
            return null;

        if (!InheritsJsonSerializerContext(type))
            return null;

        var registered = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString()
                != "System.Text.Json.Serialization.JsonSerializableAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is ITypeSymbol registeredType)
            {
                registered.Add(Normalize(registeredType));
            }
        }

        // A partial context declared in several files shows up once per part. Returning every part
        // is fine: the union across parts is what matters and duplicates are harmless.
        return new JsonContextRef(type.ToDisplayString(), registered.ToImmutable());
    }

    /// <summary>
    /// Reports every serialized type no context registers.
    /// </summary>
    /// <param name="serialized">What the contracts send and receive.</param>
    /// <param name="contexts">The contexts found in the compilation.</param>
    /// <returns>The diagnostics to report.</returns>
    internal static ImmutableArray<Diagnostic> Check(
        ImmutableArray<SerializedTypeRef> serialized,
        ImmutableArray<JsonContextRef> contexts)
    {
        // No context at all means the application is on the reflection-based serializer, which is
        // a supported choice that already carries its own trimming warning. Nagging about coverage
        // there would be advice nobody asked for.
        if (contexts.IsDefaultOrEmpty || serialized.IsDefaultOrEmpty)
            return ImmutableArray<Diagnostic>.Empty;

        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var context in contexts)
            foreach (var type in context.RegisteredTypes)
                covered.Add(type);

        // Any context counts, because nothing here can tell which context is wired to which
        // client — several may be, and guessing would invent false positives.
        var names = string.Join(", ", contexts.Select(c => c.ContextName).Distinct().OrderBy(n => n));
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in serialized)
        {
            if (RidesAlong.Contains(type.TypeName) || covered.Contains(type.TypeName))
                continue;

            // One report per type per method, so a type used by five methods does not produce five
            // identical warnings at the same location.
            if (!reported.Add($"{type.MemberDisplay}|{type.TypeName}"))
                continue;

            diagnostics.Add(Diagnostic.Create(
                Diagnostics.UncoveredSerializedType,
                type.Location,
                type.MemberDisplay,
                type.TypeName,
                $"[JsonSerializable(typeof({type.TypeName}))]",
                names));
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// Renders a type the way a <c>typeof()</c> argument renders, so the two sides compare.
    /// </summary>
    /// <remarks>
    /// Nullable annotations are dropped: a method returning <c>Task&lt;Repository?&gt;</c> needs
    /// the same registration as one returning <c>Task&lt;Repository&gt;</c>, and
    /// <c>typeof(Repository)</c> is how it is written either way. A nullable value type normalizes
    /// to its underlying type for the same reason — registering the struct covers both.
    /// </remarks>
    internal static string Normalize(ITypeSymbol type)
    {
        var unwrapped = type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } nullable
                        && nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            ? nullable.TypeArguments[0]
            : type;

        return unwrapped.WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    /// Normalizes a name the generator already rendered, which carries nullable annotations.
    /// </summary>
    /// <remarks>
    /// The model renders types with the nullable modifier included, and a <c>typeof()</c> argument
    /// never has one, so the trailing marker comes off before comparing. On a nullable value type
    /// that also lands on the underlying type, which is the right answer — registering the struct
    /// covers both.
    /// </remarks>
    internal static string Normalize(string renderedTypeName)
        => renderedTypeName.EndsWith("?", StringComparison.Ordinal)
            ? renderedTypeName.Substring(0, renderedTypeName.Length - 1)
            : renderedTypeName;

    /// <summary>A cheap syntactic filter: only a class with a base list can be a context.</summary>
    internal static bool CouldBeJsonContext(SyntaxNode node)
        => node is ClassDeclarationSyntax { BaseList: not null };

    private static bool InheritsJsonSerializerContext(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Text.Json.Serialization.JsonSerializerContext")
                return true;
        }

        return false;
    }
}
