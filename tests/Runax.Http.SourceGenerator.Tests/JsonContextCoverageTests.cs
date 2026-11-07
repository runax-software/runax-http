namespace Runax.Http.SourceGenerator.Tests;

/// <summary>
/// <c>RXH024</c>: every type a contract serializes has to be registered with a
/// <c>JsonSerializerContext</c>, or a trimmed deployment fails on the first call that needs it.
/// </summary>
/// <remarks>
/// <para>
/// These tests assert on the generator's diagnostics only, not on the resulting compilation.
/// The harness drives our generator alone, so a <c>JsonSerializerContext</c> declared in a snippet
/// is never populated by System.Text.Json's generator and the compilation carries the expected
/// <c>CS0534</c>. That is an artefact of the harness, not of the feature.
/// </para>
/// <para>
/// The deliberate exclusions get as much coverage as the positive cases, because a diagnostic that
/// fires when nothing is wrong gets suppressed wholesale and then protects nobody.
/// </para>
/// </remarks>
public class JsonContextCoverageTests
{
    private const string Context = """

        [System.Text.Json.Serialization.JsonSerializable(typeof(Thing))]
        internal sealed partial class ProbeContext : System.Text.Json.Serialization.JsonSerializerContext;
        """;

    [Fact]
    public void A_compilation_with_no_context_is_left_alone()
    {
        // The reflection-based serializer is a supported choice that already carries its own
        // trimming warning. Nagging about coverage on top of it would be advice nobody asked for.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things")]
                Task<Other> GetAsync();
            }
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Fact]
    public void A_type_no_context_registers_is_reported()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/others")]
                Task<Other> GetAsync();
            }
            {{Context}}
            """);

        run.Ids.ShouldContain("RXH024");

        var message = run.Message("RXH024");

        // The message has to carry the fix, not just the complaint: the type, the exact attribute
        // line, and which context to put it on.
        message.ShouldContain("Other");
        message.ShouldContain("[JsonSerializable(typeof(");
        message.ShouldContain("ProbeContext");
    }

    [Fact]
    public void A_registered_type_is_not_reported()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things")]
                Task<Thing> GetAsync();
            }
            {{Context}}
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Fact]
    public void A_body_is_checked_as_well_as_a_response()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Post("/things")]
                Task<Thing> CreateAsync([Body] Other body);
            }
            {{Context}}
            """);

        run.Ids.ShouldContain("RXH024");
        run.Message("RXH024").ShouldContain("Other");
    }

    [Fact]
    public void A_collection_return_type_needs_the_collection_registered()
    {
        // Registering Thing does not give you IReadOnlyList<Thing> — verified against a real
        // context, where every collection resolved to null. This is the easiest coverage gap to
        // miss by hand, which is most of why the check is worth having.
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things")]
                Task<IReadOnlyList<Thing>> ListAsync();
            }
            {{Context}}
            """);

        run.Ids.ShouldContain("RXH024");
        run.Message("RXH024").ShouldContain("IReadOnlyList");
    }

    [Fact]
    public void Response_attribute_types_are_checked()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things/{id}")]
                [Response(200, typeof(Thing))]
                [Response(404, typeof(Other))]
                Task<ApiResult> GetAsync(int id);
            }
            {{Context}}
            """);

        run.Ids.ShouldContain("RXH024");
        run.Message("RXH024").ShouldContain("Other");
    }

    [Fact]
    public void An_ApiResponse_content_type_is_checked()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things")]
                Task<ApiResponse<Other>> TryGetAsync();
            }
            {{Context}}
            """);

        run.Ids.ShouldContain("RXH024");
    }

    [Theory]
    [InlineData("BodyFormat.Raw", "string")]
    [InlineData("BodyFormat.UrlEncoded", "Dictionary<string, string>")]
    public void A_body_that_never_reaches_the_serializer_is_not_checked(string format, string type)
    {
        // Raw is already bytes; a form body is built from the argument's own shape. Neither goes
        // anywhere near IContentSerializer, so registering them would be cargo cult.
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Post("/things")]
                Task<Thing> SendAsync([Body(Format = {{format}})] {{type}} body);
            }
            {{Context}}
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Fact]
    public void A_method_with_no_response_body_is_not_checked()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Delete("/things/{id}")]
                Task DeleteAsync(int id);

                [Head("/things/{id}")]
                Task<ApiResponse> ExistsAsync(int id);
            }
            {{Context}}
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("bool")]
    public void A_bare_primitive_response_is_not_reported(string type)
    {
        // These ride along whenever any registered type has a property of that type, which in a
        // real context is close enough to always. A probe confirmed a context registering one
        // record resolved its property types for free. Reporting them would be noise, and noise
        // gets a diagnostic suppressed wholesale.
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/value")]
                Task<{{type}}> GetAsync();
            }
            {{Context}}
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Fact]
    public void A_nullable_response_matches_a_non_nullable_registration()
    {
        // typeof(Thing) is how the registration is written either way, so the annotation must not
        // make a covered type look uncovered.
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things")]
                Task<Thing?> GetAsync();
            }
            {{Context}}
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Fact]
    public void Any_context_in_the_compilation_counts()
    {
        // Nothing here can tell which context is wired to which client, and several may be. Taking
        // the union is the only choice that cannot invent a false positive.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/things")]
                Task<Thing> GetThingAsync();

                [Get("/others")]
                Task<Other> GetOtherAsync();
            }

            [System.Text.Json.Serialization.JsonSerializable(typeof(Thing))]
            internal sealed partial class FirstContext : System.Text.Json.Serialization.JsonSerializerContext;

            [System.Text.Json.Serialization.JsonSerializable(typeof(Other))]
            internal sealed partial class SecondContext : System.Text.Json.Serialization.JsonSerializerContext;
            """);

        run.Ids.ShouldNotContain("RXH024");
    }

    [Fact]
    public void One_type_used_by_many_methods_is_reported_once_per_method()
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IClient
            {
                [Get("/a")]
                Task<Other> GetAAsync();

                [Get("/b")]
                Task<Other> GetBAsync();
            }
            {{Context}}
            """);

        // Two methods, two reports — not four, and not one. A single report would hide the second
        // call site; duplicates per method would be noise.
        run.Diagnostics.Count(d => d.Id == "RXH024").ShouldBe(2);
    }
}
