namespace Runax.Http.SourceGenerator.Tests;

/// <summary>
/// One test per diagnostic the generator can report.
/// </summary>
/// <remarks>
/// These exist because a diagnostic nobody tests is a diagnostic that can quietly stop firing —
/// which is exactly what happened to RXH001, declared and unreachable for two releases because the
/// generator only ever visited interface declarations. The ids also reach consumers' build logs and
/// <c>NoWarn</c> lists, so they are API: retiring one or changing its severity is a breaking change,
/// and these tests are what makes that visible in review.
/// </remarks>
public class DiagnosticTests
{
    [Fact]
    public void A_valid_contract_produces_no_diagnostics()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGoodClient
            {
                [Get("/things/{id}")]
                Task<Thing> GetAsync(int id, CancellationToken ct = default);

                [Post("/things")]
                Task<Thing> CreateAsync([Body] Thing thing, CancellationToken ct = default);

                [Get("/things")]
                Task<IReadOnlyList<Thing>> ListAsync([Query] string? filter = null, CancellationToken ct = default);

                [Delete("/things/{id}")]
                Task DeleteAsync(int id, CancellationToken ct = default);

                [Get("/things/{id}/maybe")]
                Task<ApiResponse<Thing>> TryGetAsync(int id, CancellationToken ct = default);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.Sources.ShouldNotBeEmpty();

        // Generated code that does not compile is worse than no generated code, so the whole
        // updated compilation has to be clean too.
        run.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Code_with_no_client_interface_is_left_alone()
    {
        var run = GeneratorHarness.Run("""
            namespace Probe;

            public interface INotAClient
            {
                void DoSomething();
            }

            public class Ordinary
            {
                public int Value { get; set; }
            }
            """);

        // Every other interface in the compilation has to pass through untouched and silent.
        run.Ids.ShouldBeEmpty();
        run.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void RXH001_request_attributes_on_a_class()
    {
        var run = GeneratorHarness.RunContract("""
            public class NotAnInterface
            {
                [Get("/things")]
                public Task<Thing> GetAsync() => null!;
            }
            """);

        run.Ids.ShouldContain("RXH001");
        run.Message("RXH001").ShouldContain("interface");
    }

    [Fact]
    public void RXH002_a_synchronous_return_type()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Thing Get();
            }
            """);

        run.Ids.ShouldContain("RXH002");
        run.Message("RXH002").ShouldContain("Task");
    }

    [Theory]
    [InlineData("void Go();")]
    [InlineData("int Count();")]
    [InlineData("ValueTask<Thing> GetAsync();")]
    [InlineData("Task<Thing>[] Many();")]
    public void RXH002_covers_every_shape_that_is_not_a_Task(string signature)
    {
        var run = GeneratorHarness.RunContract($$"""
            public interface IBad
            {
                [Get("/things")]
                {{signature}}
            }
            """);

        run.Ids.ShouldContain("RXH002");
    }

    [Fact]
    public void RXH003_a_placeholder_with_no_parameter()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/repos/{owner}/{name}")]
                Task<Thing> GetAsync(string owner);
            }
            """);

        run.Ids.ShouldContain("RXH003");

        // The message has to name the placeholder; "a placeholder is unmatched" sends the reader
        // back to count braces.
        run.Message("RXH003").ShouldContain("{name}");
    }

    [Fact]
    public void RXH004_more_than_one_body()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Post("/things")]
                Task<Thing> CreateAsync([Body] Thing a, [Body] Other b);
            }
            """);

        run.Ids.ShouldContain("RXH004");
    }

    [Fact]
    public void RXH005_a_parameter_bound_to_nothing()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync(int mystery);
            }
            """);

        run.Ids.ShouldContain("RXH005");
        run.Message("RXH005").ShouldContain("mystery");
    }

    [Fact]
    public void An_unannotated_parameter_matching_a_placeholder_is_fine()
    {
        // The convenience that makes RXH005 tolerable: repeating the name in a [Path] attribute is
        // noise, so a parameter named after a placeholder binds to it.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Get("/things/{id}")]
                Task<Thing> GetAsync(int id);
            }
            """);

        run.Ids.ShouldBeEmpty();
    }

    [Fact]
    public void RXH006_more_than_one_cancellation_token()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync(CancellationToken a, CancellationToken b);
            }
            """);

        run.Ids.ShouldContain("RXH006");
    }

    [Fact]
    public void RXH007_a_generic_interface()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad<T>
            {
                [Get("/things")]
                Task<Thing> GetAsync();
            }
            """);

        run.Ids.ShouldContain("RXH007");
    }

    [Fact]
    public void RXH008_a_generic_method()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<T> GetAsync<T>();
            }
            """);

        run.Ids.ShouldContain("RXH008");
    }

    [Fact]
    public void RXH009_a_member_with_no_request_attribute()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync();

                Task<int> CountAsync();
            }
            """);

        run.Ids.ShouldContain("RXH009");
    }

    [Fact]
    public void RXH010_a_body_on_a_get_is_a_warning_not_an_error()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IOdd
            {
                [Get("/search")]
                Task<Thing> SearchAsync([Body] Other query);
            }
            """);

        run.Ids.ShouldContain("RXH010");

        // A warning because some APIs genuinely do this, so an error would be wrong — and the
        // client still has to be generated.
        var warning = run.Diagnostics.First(diagnostic => diagnostic.Id == "RXH010");
        warning.Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        run.Sources.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Patch")]
    [InlineData("Delete")]
    public void A_body_on_a_method_that_carries_one_is_not_warned_about(string verb)
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IFine
            {
                [{{verb}}("/things")]
                Task<Thing> SendAsync([Body] Thing thing);
            }
            """);

        run.Ids.ShouldBeEmpty();
    }

    [Fact]
    public void RXH011_a_base_address_that_is_not_absolute()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("api.example.com")]
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync();
            }
            """);

        run.Ids.ShouldContain("RXH011");
    }

    [Fact]
    public void RXH012_two_parameters_bound_to_one_placeholder()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things/{id}")]
                Task<Thing> GetAsync([Path("id")] int a, [Path("id")] int b);
            }
            """);

        run.Ids.ShouldContain("RXH012");
    }

    [Fact]
    public void RXH013_a_property_on_a_client_interface()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync();

                string BaseUrl { get; }
            }
            """);

        run.Ids.ShouldContain("RXH013");
        run.Message("RXH013").ShouldContain("property");
    }

    [Fact]
    public void RXH013_an_event_on_a_client_interface()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync();

                event EventHandler Sent;
            }
            """);

        run.Ids.ShouldContain("RXH013");
        run.Message("RXH013").ShouldContain("event");
    }

    [Fact]
    public void A_form_body_over_key_value_pairs_is_accepted()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Post("/things")]
                Task<Thing> SendAsync([Body(Format = BodyFormat.UrlEncoded)] Dictionary<string, string> fields);

                [Post("/other")]
                Task<Thing> SendObjectsAsync(
                    [Body(Format = BodyFormat.UrlEncoded)] IEnumerable<KeyValuePair<string, object?>> fields);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.AllSource.ShouldContain("FormBody.Create");
    }

    [Fact]
    public void A_multipart_body_over_parts_is_accepted()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Post("/upload")]
                Task<Thing> UploadAsync([Body(Format = BodyFormat.Multipart)] IReadOnlyList<MultipartPart> parts);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.AllSource.ShouldContain("MultipartBody.Create");
    }

    [Fact]
    public void RXH019_a_form_body_that_is_not_key_value_pairs()
    {
        // Mapping an object's properties to form fields would mean reflecting over the type at run
        // time, which is the one thing this library does not do — so it is a build error rather
        // than a trimming surprise.
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Post("/things")]
                Task<Thing> SendAsync([Body(Format = BodyFormat.UrlEncoded)] Thing thing);
            }
            """);

        run.Ids.ShouldContain("RXH019");
        run.Message("RXH019").ShouldContain("key/value pairs");
        run.Message("RXH019").ShouldContain("BodyFormat.Json");
    }

    [Fact]
    public void RXH019_a_multipart_body_that_is_not_parts()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Post("/upload")]
                Task<Thing> UploadAsync([Body(Format = BodyFormat.Multipart)] Thing thing);
            }
            """);

        run.Ids.ShouldContain("RXH019");
        run.Message("RXH019").ShouldContain("MultipartPart");
    }

    [Theory]
    [InlineData("string")]
    [InlineData("byte[]")]
    [InlineData("Stream")]
    [InlineData("MemoryStream")]
    public void A_raw_body_of_a_sendable_type_is_accepted(string type)
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Put("/raw")]
                Task SendAsync([Body(Format = BodyFormat.Raw)] {{type}} payload);
            }
            """);

        run.Ids.ShouldBeEmpty();
    }

    [Fact]
    public void RXH019_a_raw_body_that_is_not_in_wire_form()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Put("/raw")]
                Task SendAsync([Body(Format = BodyFormat.Raw)] Thing thing);
            }
            """);

        run.Ids.ShouldContain("RXH019");
        run.Message("RXH019").ShouldContain("sent as written");
    }

    [Fact]
    public void A_retry_on_an_idempotent_method_is_accepted()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Get("/reports/{id}")]
                [Retry(5, DelayMilliseconds = 250)]
                Task<Thing> GetAsync(int id);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.AllSource.ShouldContain("RetryOverride(5, 250, false)");
    }

    [Fact]
    public void A_retry_of_one_attempt_is_meaningful_rather_than_a_mistake()
    {
        // It exempts this method from a client-wide retry policy, which is a real thing to want.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Delete("/things/{id}")]
                [Retry(1)]
                Task DeleteAsync(int id);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.AllSource.ShouldContain("RetryOverride(1,");
    }

    [Theory]
    [InlineData("Post")]
    [InlineData("Patch")]
    public void RXH020_a_retry_on_a_non_idempotent_method_does_nothing(string verb)
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IOdd
            {
                [{{verb}}("/things")]
                [Retry(3)]
                Task<Thing> SendAsync([Body] Thing thing);
            }
            """);

        // The safety default would leave the attribute doing nothing at all, and nothing else in
        // the build would mention it.
        run.Ids.ShouldContain("RXH020");
        run.Message("RXH020").ShouldContain("AllowNonIdempotent");

        var warning = run.Diagnostics.First(diagnostic => diagnostic.Id == "RXH020");
        warning.Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        // A warning, so the client is still generated.
        run.Sources.ShouldNotBeEmpty();
    }

    [Fact]
    public void An_explicit_opt_in_on_a_non_idempotent_method_is_not_warned_about()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Post("/things")]
                [Retry(3, AllowNonIdempotent = true)]
                Task<Thing> SendAsync([Body] Thing thing);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.AllSource.ShouldContain("RetryOverride(3, -1, true)");
    }

    [Fact]
    public void A_single_attempt_on_a_non_idempotent_method_is_not_warned_about()
    {
        // [Retry(1)] asks for no retries, so the safety default is not suppressing anything.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Post("/things")]
                [Retry(1)]
                Task<Thing> SendAsync([Body] Thing thing);
            }
            """);

        run.Ids.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RXH021_a_retry_below_one_attempt(int attempts)
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IBad
            {
                [Get("/things")]
                [Retry({{attempts}})]
                Task<Thing> GetAsync();
            }
            """);

        run.Ids.ShouldContain("RXH021");
        run.Message("RXH021").ShouldContain("include the first");
    }

    [Fact]
    public void RXH014_is_retired()
    {
        // It refused the two body formats that are now emitted. Recorded as removed in
        // AnalyzerReleases rather than quietly dropped, and its number is not reused.
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Post("/things")]
                Task<Thing> SendAsync([Body(Format = BodyFormat.UrlEncoded)] Dictionary<string, string> fields);
            }
            """);

        run.Ids.ShouldNotContain("RXH014");
    }

    [Fact]
    public void RXH015_Response_without_an_ApiResult_return_type()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things/{id}")]
                [Response(200, typeof(Thing))]
                Task<Thing> GetAsync(int id);
            }
            """);

        run.Ids.ShouldContain("RXH015");
    }

    [Fact]
    public void RXH016_ApiResult_without_any_Response()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things/{id}")]
                Task<ApiResult> GetAsync(int id);
            }
            """);

        run.Ids.ShouldContain("RXH016");
    }

    [Fact]
    public void RXH017_two_responses_with_the_same_condition()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things/{id}")]
                [Response(200, typeof(Thing))]
                [Response(200, typeof(Other))]
                Task<ApiResult> GetAsync(int id);
            }
            """);

        run.Ids.ShouldContain("RXH017");
        run.Message("RXH017").ShouldContain("200");
    }

    [Fact]
    public void RXH018_a_response_after_the_fallback_can_never_match()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IBad
            {
                [Get("/things/{id}")]
                [Response(typeof(Thing))]
                [Response(404, typeof(Other))]
                Task<ApiResult> GetAsync(int id);
            }
            """);

        run.Ids.ShouldContain("RXH018");

        var warning = run.Diagnostics.First(diagnostic => diagnostic.Id == "RXH018");
        warning.Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Responses_in_the_right_order_are_not_warned_about()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Get("/things/{id}")]
                [Response(404, typeof(Other))]
                [Response(typeof(ApiError), WhenJsonProperty = "error")]
                [Response(typeof(Thing))]
                Task<ApiResult> GetAsync(int id);
            }

            public sealed record ApiError(string Error);
            """);

        run.Ids.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("int?")]
    [InlineData("bool")]
    [InlineData("global::System.Guid")]
    [InlineData("global::System.DateTimeOffset")]
    [InlineData("global::System.Uri")]
    [InlineData("global::System.Collections.Generic.IEnumerable<string>")]
    [InlineData("string[]")]
    public void A_sendable_query_type_is_accepted(string type)
    {
        var run = GeneratorHarness.RunContract($$"""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] {{type}} value);
            }
            """);

        run.Ids.ShouldBeEmpty();
    }

    [Fact]
    public void A_query_object_is_expanded_rather_than_rejected()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] Filter filter);
            }

            public sealed record Filter(string State, int Page);
            """);

        run.Ids.ShouldBeEmpty();

        // One key per property, resolved at compile time — which is the whole point: no reflection.
        run.AllSource.ShouldContain("\"State\"");
        run.AllSource.ShouldContain("\"Page\"");
        run.AllSource.ShouldNotContain("\"filter\"");
    }

    [Fact]
    public void A_dictionary_query_parameter_is_spread_at_run_time()
    {
        var run = GeneratorHarness.RunContract("""
            [BaseAddress("https://api.example.com")]
            public interface IGood
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] Dictionary<string, string> filters);
            }
            """);

        run.Ids.ShouldBeEmpty();
        run.AllSource.ShouldContain("QueryFields.From");
    }

    [Fact]
    public void RXH022_a_query_parameter_that_cannot_be_sent()
    {
        // The bug this replaced: a type with no usable properties previously fell through to
        // ToString() and the server received "Opaque" with no warning anywhere.
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] Opaque value);
            }

            public sealed class Opaque
            {
                private int Hidden { get; set; }
            }
            """);

        run.Ids.ShouldContain("RXH022");
        run.Message("RXH022").ShouldContain("query string");
    }

    [Fact]
    public void RXH022_a_collection_of_objects_cannot_be_sent()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] IEnumerable<Filter> filters);
            }

            public sealed record Filter(string State);
            """);

        run.Ids.ShouldContain("RXH022");
        run.Message("RXH022").ShouldContain("items");
    }

    [Fact]
    public void RXH023_a_query_object_with_a_nested_object_property()
    {
        // Refused rather than flattened: ASP.NET Core spells a nested key "filter.state" and
        // OpenAPI's deepObject spells it "filter[state]", so flattening would have to guess.
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] Outer filter);
            }

            public sealed record Inner(string State);
            public sealed record Outer(Inner Nested, int Page);
            """);

        run.Ids.ShouldContain("RXH023");
        run.Message("RXH023").ShouldContain("Nested");
        run.Message("RXH023").ShouldContain("nested field");
    }

    [Fact]
    public void RXH023_names_the_property_that_cannot_be_sent()
    {
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/things")]
                Task<Thing> GetAsync([Query] Filter filter);
            }

            public sealed record Filter(string State, System.Text.StringBuilder Builder);
            """);

        run.Ids.ShouldContain("RXH023");
        run.Message("RXH023").ShouldContain("Builder");
    }

    [Fact]
    public void Every_diagnostic_reports_a_location_in_the_user_code()
    {
        // A diagnostic with no location shows up at the top of the build log attached to nothing,
        // which is how a clear message becomes a scavenger hunt.
        var run = GeneratorHarness.RunContract("""
            public interface IBad
            {
                [Get("/repos/{owner}")]
                Task<Thing> GetAsync(int mystery);
            }
            """);

        run.Diagnostics.ShouldNotBeEmpty();

        foreach (var diagnostic in run.Diagnostics)
            diagnostic.Location.ShouldNotBe(Microsoft.CodeAnalysis.Location.None, diagnostic.Id);
    }
}
