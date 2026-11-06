using Runax.Http.Requests;

namespace Runax.Http.Tests;

/// <summary>
/// URL composition, which is the single rule most worth pinning down.
/// </summary>
/// <remarks>
/// HTTP libraries disagree here in ways that are easy to miss: whether a leading slash discards
/// the base path, whether a trailing slash survives, how a query value is escaped. Composing the
/// URL once above the transport boundary is what makes a transport swap invisible — so these are
/// the tests that defend that promise.
/// </remarks>
public class RequestUriFactoryTests
{
    private static Uri Build(
        string? baseAddress,
        string template,
        IReadOnlyList<KeyValuePair<string, object?>>? path = null,
        IReadOnlyList<KeyValuePair<string, object?>>? query = null)
        => RequestUriFactory.Create(
            baseAddress is null ? null : new Uri(baseAddress),
            new ClientRequest
            {
                Method = RequestMethod.Get,
                Template = template,
                PathValues = path ?? [],
                QueryValues = query ?? []
            });

    [Theory]
    // The base path is kept in every combination of slashes. Uri's own relative resolution would
    // return https://host/users for the first two, throwing away the /api/v2 someone configured.
    [InlineData("https://host/api/v2", "/users", "https://host/api/v2/users")]
    [InlineData("https://host/api/v2/", "/users", "https://host/api/v2/users")]
    [InlineData("https://host/api/v2", "users", "https://host/api/v2/users")]
    [InlineData("https://host/api/v2/", "users", "https://host/api/v2/users")]
    [InlineData("https://host", "/users", "https://host/users")]
    [InlineData("https://host/", "/users", "https://host/users")]
    public void The_template_appends_to_the_base_path(string baseAddress, string template, string expected)
        => Build(baseAddress, template).ToString().ShouldBe(expected);

    [Fact]
    public void An_absolute_template_ignores_the_base_address()
    {
        // Occasionally an API hands back a fully qualified URL to follow; honouring it is more
        // useful than refusing it, and there is no ambiguity about what was meant.
        Build("https://host/api", "https://elsewhere.example/thing")
            .ToString().ShouldBe("https://elsewhere.example/thing");
    }

    [Fact]
    public void A_relative_template_without_a_base_address_says_what_to_do_about_it()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => Build(null, "/users"));

        thrown.Message.ShouldContain("[BaseAddress]");
        thrown.Message.ShouldContain("WithBaseAddress");
    }

    [Fact]
    public void Placeholders_are_substituted()
        => Build("https://host", "/repos/{owner}/{name}",
                path: [new("owner", "runax-software"), new("name", "runax-http")])
            .AbsolutePath.ShouldBe("/repos/runax-software/runax-http");

    [Fact]
    public void Placeholder_values_are_escaped_as_one_segment()
        => Build("https://host", "/files/{path}", path: [new("path", "a/b c?d=e")])
            .AbsolutePath.ShouldBe("/files/a%2Fb%20c%3Fd%3De");

    [Fact]
    public void A_placeholder_with_no_value_supplied_fails_loudly()
    {
        // The generator rejects a placeholder with no parameter at compile time; this is the
        // runtime half, for a caller building a ClientRequest by hand.
        var thrown = Should.Throw<InvalidOperationException>(
            () => Build("https://host", "/repos/{owner}"));

        thrown.Message.ShouldContain("{owner}");
    }

    [Fact]
    public void A_null_path_value_is_refused_rather_than_collapsing_the_segment()
    {
        // "/repos//issues" addresses something different from what the caller named, and would
        // likely 404 somewhere far from the cause.
        var thrown = Should.Throw<ArgumentException>(
            () => Build("https://host", "/repos/{owner}", path: [new("owner", null)]));

        thrown.Message.ShouldContain("null path value");
    }

    [Fact]
    public void An_unclosed_placeholder_is_reported()
        => Should.Throw<InvalidOperationException>(() => Build("https://host", "/repos/{owner"))
            .Message.ShouldContain("unclosed");

    [Fact]
    public void Query_values_are_escaped()
        => Build("https://host", "/search", query: [new("q", "a b&c=d")])
            .Query.ShouldBe("?q=a%20b%26c%3Dd");

    [Fact]
    public void A_collection_query_value_repeats_the_key()
        => Build("https://host", "/issues", query: [new("label", new[] { "bug", "docs" })])
            .Query.ShouldBe("?label=bug&label=docs");

    [Fact]
    public void A_null_query_value_contributes_nothing()
        => Build("https://host", "/issues", query: [new("label", null), new("state", "open")])
            .Query.ShouldBe("?state=open");

    [Fact]
    public void An_empty_collection_contributes_nothing()
        => Build("https://host", "/issues", query: [new("label", Array.Empty<string>())])
            .Query.ShouldBeEmpty();

    [Fact]
    public void A_query_string_in_the_template_is_kept_and_appended_to()
    {
        // Someone writing [Get("/search?scope=all")] meant both, not whichever the implementation
        // happens to keep.
        Build("https://host", "/search?scope=all", query: [new("q", "runax")])
            .Query.ShouldBe("?scope=all&q=runax");
    }

    [Fact]
    public void A_template_query_survives_with_no_arguments()
        => Build("https://host", "/search?scope=all").Query.ShouldBe("?scope=all");
}
