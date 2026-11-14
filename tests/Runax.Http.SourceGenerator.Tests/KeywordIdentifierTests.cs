namespace Runax.Http.SourceGenerator.Tests;

/// <summary>
/// A parameter or query-object property named with a C# keyword — <c>@class</c>, <c>@event</c> —
/// is legal in the interface and has to stay legal in the generated code.
/// </summary>
/// <remarks>
/// Roslyn reports <c>@class</c> as plain <c>class</c>, and the generator used to write it back
/// that way, emitting <c>string? class</c>: a syntax error in code the author never wrote. APIs
/// name query keys <c>class</c>, <c>type</c> and <c>default</c> all the time, and an interface
/// generated from an OpenAPI document reaches for the escape exactly then.
/// </remarks>
public class KeywordIdentifierTests
{
    [Fact]
    public void Keyword_named_parameters_and_query_properties_compile_and_keep_their_wire_names()
    {
        var run = GeneratorHarness.RunContract("""
            public sealed class Filter
            {
                public string? @class { get; init; }
            }

            [BaseAddress("https://api.example.com")]
            public interface IKeywords
            {
                [Get("/things/{event}")]
                Task<Thing> GetAsync(
                    string @event,
                    [Query] string? @class = null,
                    [Header("X-Default")] string? @default = null,
                    [Query] Filter? @object = null,
                    CancellationToken @checked = default);
            }
            """);

        run.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ShouldBeEmpty();

        var source = run.Sources.ShouldHaveSingleItem().Source;

        source.ShouldContain("string @event");
        source.ShouldContain("""new global::System.Collections.Generic.KeyValuePair<string, object?>("class", @class)""");
        source.ShouldContain("@object?.@class");
        source.ShouldContain("@checked");
    }
}
