using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// A whole object as a <c>[Query]</c> parameter, expanded to one key per property at compile time.
/// </summary>
/// <remarks>
/// Before this, `[Query] SearchFilter filter` compiled clean and sent
/// <c>?filter=SearchFilter%20%7B%20State%20%3D%20open...</c> — <c>ToString()</c> on the record,
/// with no diagnostic. These tests pin the shape of the query string, because that is the part a
/// server sees and the part that was silently wrong.
/// </remarks>
public class QueryObjectTests
{
    private static (ISearchClient Client, RecordingTransport Transport) Build()
    {
        var transport = new RecordingTransport(
            respond: _ => Responses.Json("""{"id":1,"name":"ok"}"""));

        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        services.AddRunaxClient<ISearchClient>();

        return (services.BuildServiceProvider().GetRequiredService<ISearchClient>(), transport);
    }

    private static string Query(RecordingTransport transport) => transport.Last.RequestUri.Query;

    [Fact]
    public async Task An_object_expands_to_one_key_per_property()
    {
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { State = "open", Page = 2, PerPage = 50 });

        // Not "?filter=SearchFilter { ... }".
        Query(transport).ShouldNotContain("filter=");
        Query(transport).ShouldContain("State=open");
        Query(transport).ShouldContain("Page=2");
    }

    [Fact]
    public async Task A_property_name_is_sent_exactly_as_written()
    {
        // Nothing in this library transforms a name: a [Query] parameter sends its parameter name
        // verbatim, so a property has to behave the same way or the two rules disagree.
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { State = "open" });

        Query(transport).ShouldContain("State=open");

        // Case-sensitively: Shouldly compares case-insensitively by default, which would let a
        // lowercased key pass the very assertion meant to catch it.
        Query(transport).ShouldNotContain("state=open", Case.Sensitive);
    }

    [Fact]
    public async Task A_property_can_be_renamed_with_Query()
    {
        // Which is how a C# PerPage reaches an API that spells it per_page.
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { PerPage = 50, SortBy = "created" });

        Query(transport).ShouldContain("per_page=50");
        Query(transport).ShouldContain("sort_by=created");
        Query(transport).ShouldNotContain("PerPage");
        Query(transport).ShouldNotContain("SortBy");
    }

    [Fact]
    public async Task An_inherited_property_is_expanded_too()
    {
        // A filter type with a shared pagination base is the obvious shape; ignoring the base would
        // drop fields silently, which is the failure this whole change exists to stop.
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { Page = 7 });

        Query(transport).ShouldContain("Page=7");
    }

    [Fact]
    public async Task A_null_property_is_omitted_rather_than_sent_empty()
    {
        // Consistent with a null [Query] parameter, which is already omitted.
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { State = null, Page = 1, PerPage = 30 });

        Query(transport).ShouldNotContain("State");
        Query(transport).ShouldNotContain("Since");
        Query(transport).ShouldContain("Page=1");
    }

    [Fact]
    public async Task A_collection_property_becomes_repeated_keys()
    {
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { Labels = ["bug", "needs triage"] });

        Query(transport).ShouldContain("Labels=bug");
        Query(transport).ShouldContain("Labels=needs%20triage");
    }

    [Fact]
    public async Task Property_values_are_formatted_like_every_other_query_value()
    {
        // The formatter is shared, so a DateTimeOffset property has to spell the same thing a
        // DateTimeOffset parameter does — invariant, round-trippable, lowercase bool.
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter
        {
            Since = new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero),
            Archived = false,
            Order = SearchOrder.Descending
        });

        Query(transport).ShouldContain("Since=2026-11-07T09%3A30%3A00.0000000%2B00%3A00");
        Query(transport).ShouldContain("Archived=false");
        Query(transport).ShouldContain("Order=Descending");
    }

    [Fact]
    public async Task A_null_object_contributes_nothing()
    {
        var (client, transport) = Build();

        await client.SearchNullableAsync(null);

        Query(transport).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_struct_object_expands_without_null_checks()
    {
        // The non-nullable value-type access path: `point.X` rather than `point?.X`, which would
        // not compile.
        var (client, transport) = Build();

        await client.SearchPointAsync(new PointFilter(3, 4));

        Query(transport).ShouldBe("?X=3&Y=4");
    }

    [Fact]
    public async Task A_nullable_struct_object_expands_when_present()
    {
        var (client, transport) = Build();

        await client.SearchNullablePointAsync(new PointFilter(1, 2));

        Query(transport).ShouldBe("?X=1&Y=2");
    }

    [Fact]
    public async Task A_nullable_struct_object_contributes_nothing_when_absent()
    {
        var (client, transport) = Build();

        await client.SearchNullablePointAsync(null);

        Query(transport).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dictionary_contributes_its_own_keys()
    {
        // The one shape whose keys are not known until run time, which is why it is spread in
        // rather than emitted field by field.
        var (client, transport) = Build();

        await client.SearchDynamicAsync(new Dictionary<string, string?>
        {
            ["state"] = "open",
            ["author"] = "someone"
        });

        Query(transport).ShouldContain("state=open");
        Query(transport).ShouldContain("author=someone");
        Query(transport).ShouldNotContain("filters=");
    }

    [Fact]
    public async Task A_dictionary_value_is_formatted_and_a_null_one_omitted()
    {
        var (client, transport) = Build();

        await client.SearchDynamicTypedAsync(new Dictionary<string, object?>
        {
            ["page"] = 3,
            ["archived"] = true,
            ["missing"] = null
        });

        Query(transport).ShouldContain("page=3");
        Query(transport).ShouldContain("archived=true");
        Query(transport).ShouldNotContain("missing");
    }

    [Fact]
    public async Task An_empty_dictionary_contributes_nothing()
    {
        var (client, transport) = Build();

        await client.SearchDynamicAsync([]);

        Query(transport).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_scalar_alongside_an_object_keeps_working()
    {
        var (client, transport) = Build();

        await client.SearchMixedAsync("runax", new SearchFilter { State = "open" });

        // Order follows the parameter list, so the scalar comes first.
        Query(transport).ShouldStartWith("?q=runax");
        Query(transport).ShouldContain("State=open");
    }

    [Fact]
    public async Task Keys_and_values_are_still_escaped()
    {
        var (client, transport) = Build();

        await client.SearchAsync(new SearchFilter { State = "a b&c=d" });

        Query(transport).ShouldContain("State=a%20b%26c%3Dd");
    }
}
