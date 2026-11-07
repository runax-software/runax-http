using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Runax.Http.SourceGenerator;

namespace Runax.Http.SourceGenerator.Tests;

/// <summary>What one generator run produced.</summary>
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<(string HintName, string Source)> Sources,
    ImmutableArray<Diagnostic> CompilationDiagnostics)
{
    /// <summary>The distinct Runax.Http diagnostic ids the generator reported.</summary>
    internal IReadOnlyCollection<string> Ids =>
        Diagnostics.Select(diagnostic => diagnostic.Id).Distinct().OrderBy(id => id).ToList();

    /// <summary>The message for one reported id, for asserting it says something useful.</summary>
    internal string Message(string id) =>
        Diagnostics.First(diagnostic => diagnostic.Id == id).GetMessage();

    /// <summary>Everything the generator emitted, concatenated.</summary>
    internal string AllSource => string.Concat(Sources.Select(source => source.Source));
}

/// <summary>
/// Compiles a source snippet with the generator attached and reports what came out.
/// </summary>
/// <remarks>
/// Driving the generator directly rather than asserting on a real project's build output: a test
/// has to be able to feed it code that does not compile, which is most of what the diagnostics are
/// for.
/// </remarks>
internal static class GeneratorHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    internal static GeneratorRun Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new ClientGenerator());

        var result = driver
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _)
            .GetRunResult();

        var sources = result.Results
            .SelectMany(generated => generated.GeneratedSources)
            .Select(generated => (generated.HintName, generated.SourceText.ToString()))
            .ToImmutableArray();

        return new GeneratorRun(
            result.Diagnostics,
            sources,
            updated.GetDiagnostics());
    }

    /// <summary>
    /// Wraps a client interface in the usings every snippet needs, so the tests themselves stay
    /// about the thing being tested.
    /// </summary>
    internal static GeneratorRun RunContract(string body) => Run($$"""
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using Runax.Http;

        namespace Probe;

        public sealed record Thing(int Id, string Name);
        public sealed record Other(string Message);

        {{body}}
        """);

    /// <summary>
    /// Every assembly already loaded, plus the two Runax packages. TRUSTED_PLATFORM_ASSEMBLIES is
    /// the reference set the test host itself was compiled against, which is a closer match to a
    /// real consumer than hand-picking facades.
    /// </summary>
    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (var path in platform.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return references.ToImmutable();
    }
}
