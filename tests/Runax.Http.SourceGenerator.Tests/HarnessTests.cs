namespace Runax.Http.SourceGenerator.Tests;

/// <summary>
/// Tests for the harness itself.
/// </summary>
/// <remarks>
/// A harness whose reference list silently came up empty would make every "produces no
/// diagnostics" test pass for the wrong reason: the generator would find no request attributes,
/// report nothing, and emit nothing. These assert that the setup is real, so the rest of the suite
/// means what it says.
/// </remarks>
public class HarnessTests
{
    [Fact]
    public void The_compilation_under_test_resolves_the_attributes()
    {
        // If Runax.Http.Abstractions were missing from the references, [Get] would be an unresolved
        // name and every diagnostic test would be vacuous.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IResolves
            {
                [Get("/things")]
                Task<Thing> GetAsync();
            }
            """);

        run.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Id is "CS0246" or "CS0234")
            .ShouldBeEmpty("the attributes and runtime have to be on the reference list");

        run.Sources.ShouldNotBeEmpty("a valid contract has to produce a generated client");
    }

    [Fact]
    public void The_harness_surfaces_compilation_errors()
    {
        // Proves CompilationDiagnostics is actually populated, so asserting it is empty elsewhere
        // is a real check rather than a look at an empty list.
        var run = GeneratorHarness.Run("namespace Probe; public class Broken { this is not C# }");

        run.CompilationDiagnostics
            .Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ShouldBeTrue();
    }

    [Fact]
    public void The_generated_client_is_named_after_the_interface()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IWidgetClient
            {
                [Get("/widgets")]
                Task<Thing> ListAsync();
            }
            """);

        run.Sources.ShouldHaveSingleItem();
        run.Sources[0].HintName.ShouldBe("IWidgetClient.RunaxClient.g.cs");
        run.AllSource.ShouldContain("class WidgetClientRunaxClient");
    }

    [Fact]
    public void The_generated_client_registers_itself_for_lookup()
    {
        // The module initializer is how AddRunaxClient<T>() finds an implementation without
        // reflection. If it stopped being emitted, registration would fail at run time with
        // "no generated implementation was found" and nothing would catch it at build time.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            [Transport("restsharp")]
            public interface IWidgetClient
            {
                [Get("/widgets")]
                Task<Thing> ListAsync();
            }
            """);

        run.AllSource.ShouldContain("ModuleInitializer");
        run.AllSource.ShouldContain("GeneratedClientRegistry.Register");
        run.AllSource.ShouldContain("BaseAddress = \"https://api.example.com\"");
        run.AllSource.ShouldContain("TransportName = \"restsharp\"");
    }

    [Fact]
    public void Declared_transport_names_are_emitted_for_startup_validation()
    {
        // What lets a misspelled method-level [Transport] fail when transports resolve rather than
        // on the one call that uses it.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IWidgetClient
            {
                [Get("/widgets")]
                [Transport("restsharp")]
                Task<Thing> ListAsync();
            }
            """);

        run.AllSource.ShouldContain("DeclaredTransportNames");
        run.AllSource.ShouldContain("\"restsharp\"");
    }

    [Fact]
    public void Nullable_annotations_survive_into_the_generated_signature()
    {
        // Dropping them produces a CS8767 nullability mismatch on every optional reference
        // parameter, which is how the generated client stops compiling for its consumer.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IWidgetClient
            {
                [Get("/widgets")]
                Task<Thing> ListAsync([Query] string? filter, CancellationToken ct = default);
            }
            """);

        run.AllSource.ShouldContain("string? filter");

        run.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Id is "CS8767" or "CS8766")
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_generated_code_contains_nothing_reflective()
    {
        // The central AOT claim. typeof() appears once, naming the contract in the registration;
        // anything that walks members at run time would break trimming.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IWidgetClient
            {
                [Get("/widgets/{id}")]
                Task<Thing> GetAsync(int id, [Query] string? q = null, CancellationToken ct = default);

                [Post("/widgets")]
                Task<Thing> CreateAsync([Body] Thing thing, CancellationToken ct = default);
            }
            """);

        foreach (var reflective in new[]
                 {
                     "GetCustomAttribute", "GetProperties", "GetMethods", "GetFields",
                     "Activator.CreateInstance", "MakeGenericType", "GetType()"
                 })
        {
            run.AllSource.ShouldNotContain(reflective);
        }
    }
}
