using System.Globalization;
using Runax.Http.Requests;

namespace Runax.Http.Tests;

/// <summary>
/// How an argument becomes the text on the wire.
/// </summary>
/// <remarks>
/// All invariant, deliberately. There is no case where a caller wants their machine's locale to
/// decide what a remote API receives, and a decimal point that turns into a comma on one developer's
/// machine is the kind of bug that only reproduces on their machine.
/// </remarks>
public class RequestValueFormatterTests
{
    private enum IssueState
    {
        Open,
        Closed
    }

    [Fact]
    public void Null_means_omit_rather_than_empty()
        => RequestValueFormatter.Format(null).ShouldBeNull();

    [Fact]
    public void Strings_pass_through_unchanged()
        => RequestValueFormatter.Format("already text").ShouldBe("already text");

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Booleans_are_lowercase(bool value, string expected)
    {
        // .NET spells these "True"/"False"; every JSON and OpenAPI-described API expects lowercase,
        // and the ones that accept "True" do so by accident.
        RequestValueFormatter.Format(value).ShouldBe(expected);
    }

    [Fact]
    public void Numbers_are_invariant()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            // A culture where the decimal separator is a comma. Without invariant formatting this
            // would send "1,5", which most APIs read as a list or reject outright.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            RequestValueFormatter.Format(1.5).ShouldBe("1.5");
            RequestValueFormatter.Format(1.5m).ShouldBe("1.5");
            RequestValueFormatter.Format(42).ShouldBe("42");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Dates_are_round_trippable()
    {
        RequestValueFormatter.Format(new DateTime(2026, 11, 7, 9, 30, 0, DateTimeKind.Utc))
            .ShouldBe("2026-11-07T09:30:00.0000000Z");

        RequestValueFormatter.Format(new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.FromHours(2)))
            .ShouldBe("2026-11-07T09:30:00.0000000+02:00");

        RequestValueFormatter.Format(new DateOnly(2026, 11, 7)).ShouldBe("2026-11-07");
    }

    [Fact]
    public void Guids_use_the_conventional_hyphenated_form()
        => RequestValueFormatter.Format(Guid.Parse("8ec0d1b1-7b2a-4c2e-9f3a-0d1e2f3a4b5c"))
            .ShouldBe("8ec0d1b1-7b2a-4c2e-9f3a-0d1e2f3a4b5c");

    [Fact]
    public void Enums_are_sent_as_their_member_name()
    {
        // An API that documents state=open wants the name, not "0". Enum implements IFormattable,
        // so this only holds because enums are matched before that case.
        RequestValueFormatter.Format(IssueState.Closed).ShouldBe("Closed");
    }

    [Fact]
    public void A_string_is_one_value_rather_than_a_sequence_of_characters()
        => RequestValueFormatter.Expand("abc").ShouldBe(["abc"]);

    [Fact]
    public void A_collection_expands_to_one_entry_per_item()
        => RequestValueFormatter.Expand(new[] { 1, 2, 3 }).ShouldBe(["1", "2", "3"]);

    [Fact]
    public void Nulls_inside_a_collection_are_skipped()
        => RequestValueFormatter.Expand(new string?[] { "a", null, "b" }).ShouldBe(["a", "b"]);

    [Fact]
    public void A_null_collection_expands_to_nothing()
        => RequestValueFormatter.Expand(null).ShouldBeEmpty();

    [Fact]
    public void A_scalar_expands_to_one_entry()
        => RequestValueFormatter.Expand(7).ShouldBe(["7"]);
}
