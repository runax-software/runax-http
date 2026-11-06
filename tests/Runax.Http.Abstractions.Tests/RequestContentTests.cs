using System.Text;
using Runax.Http.Transports;

namespace Runax.Http.Abstractions.Tests;

public class RequestContentTests
{
    [Fact]
    public void FromString_encodes_as_utf8()
    {
        var content = RequestContent.FromString("héllo", "text/plain");

        Encoding.UTF8.GetString(content.Bytes.ToArray()).ShouldBe("héllo");
        content.ContentType.ShouldBe("text/plain");
    }

    [Fact]
    public void Content_type_is_null_when_the_caller_did_not_pick_one()
    {
        // Null means "transport default" rather than a guess made here, so the transport can
        // apply whatever its library does for this kind of body.
        RequestContent.FromBytes(new byte[] { 1, 2, 3 }).ContentType.ShouldBeNull();
    }

    [Fact]
    public void Form_and_multipart_content_types_are_fixed_by_their_format()
    {
        RequestContent.FromForm([]).ContentType.ShouldBe("application/x-www-form-urlencoded");
        RequestContent.FromMultipart([]).ContentType.ShouldBe("multipart/form-data");
    }

    [Fact]
    public void Form_fields_keep_their_order_and_allow_repeated_keys()
    {
        var content = RequestContent.FromForm(
        [
            new KeyValuePair<string, string?>("tag", "a"),
            new KeyValuePair<string, string?>("tag", "b"),
            new KeyValuePair<string, string?>("name", null)
        ]);

        content.Fields.Count.ShouldBe(3);
        content.Fields[0].Value.ShouldBe("a");
        content.Fields[1].Value.ShouldBe("b");
        content.Fields[2].Value.ShouldBeNull();
    }

    [Fact]
    public void A_streamed_body_holds_the_stream_without_reading_it()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var content = RequestContent.FromStream(stream, "application/octet-stream");

        content.Stream.ShouldBeSameAs(stream);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void A_multipart_part_needs_a_name_and_content()
    {
        Should.Throw<ArgumentException>(() => new MultipartPart("", RequestContent.FromString("x")));
        Should.Throw<ArgumentNullException>(() => new MultipartPart("file", null!));
    }

    [Fact]
    public void A_multipart_part_carries_an_optional_file_name()
    {
        var part = new MultipartPart("file", RequestContent.FromString("x"), "report.csv");
        part.FileName.ShouldBe("report.csv");

        new MultipartPart("field", RequestContent.FromString("x")).FileName.ShouldBeNull();
    }

    [Fact]
    public void A_stream_body_cannot_be_null()
        => Should.Throw<ArgumentNullException>(() => RequestContent.FromStream(null!));

    [Fact]
    public void The_content_hierarchy_is_closed_so_a_transport_switch_is_exhaustive()
    {
        // Only this assembly can add a content kind. A transport's switch over the kinds is
        // therefore complete as written, and a new kind becomes a visible break rather than a
        // silent fall through to a default branch.
        var kinds = typeof(RequestContent).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(RequestContent)))
            .ToList();

        kinds.Count.ShouldBe(4);
        kinds.ShouldAllBe(t => t.IsSealed);
        kinds.ShouldAllBe(t => t.GetConstructors().Length == 0);
    }
}
