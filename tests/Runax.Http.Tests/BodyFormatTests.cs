using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// Form and multipart bodies, which are built from the argument's own shape rather than
/// serialized.
/// </summary>
/// <remarks>
/// Both stay structured all the way to the transport: field encoding and boundary generation are
/// genuinely native to each HTTP library, so handing it the parts beats handing it bytes.
/// </remarks>
public class BodyFormatTests
{
    private static (IFormClient Client, RecordingTransport Transport) Build()
    {
        var transport = new RecordingTransport(
            capabilities: TransportCapabilities.Multipart,
            respond: _ => Responses.Json("""{"id":1,"name":"ok"}"""));

        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        services.AddRunaxClient<IFormClient>();

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IFormClient>(), transport);
    }

    [Fact]
    public async Task A_dictionary_becomes_form_fields()
    {
        var (client, transport) = Build();

        await client.SubmitAsync(new Dictionary<string, string>
        {
            ["name"] = "a b",
            ["tag"] = "x&y"
        });

        var content = transport.Last.Content.ShouldBeOfType<FormContent>();

        // Unencoded at this point on purpose: the transport encodes, because that is the part each
        // HTTP library does natively.
        content.ContentType.ShouldBe("application/x-www-form-urlencoded");
        content.Fields.ShouldContain(field => field.Key == "name" && field.Value == "a b");
        content.Fields.ShouldContain(field => field.Key == "tag" && field.Value == "x&y");
    }

    [Fact]
    public async Task Form_values_are_formatted_the_same_way_as_query_values()
    {
        // A form that formatted dates differently from the query parameters beside it would be its
        // own small nightmare to debug.
        var (client, transport) = Build();

        await client.SubmitTypedAsync(new Dictionary<string, object?>
        {
            ["when"] = new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero),
            ["flag"] = true,
            ["count"] = 42
        });

        var content = transport.Last.Content.ShouldBeOfType<FormContent>();

        content.Fields.ShouldContain(f => f.Key == "when" && f.Value == "2026-11-07T09:30:00.0000000+00:00");
        content.Fields.ShouldContain(f => f.Key == "flag" && f.Value == "true");
        content.Fields.ShouldContain(f => f.Key == "count" && f.Value == "42");
    }

    [Fact]
    public async Task A_collection_form_value_becomes_repeated_fields()
    {
        var (client, transport) = Build();

        await client.SubmitTypedAsync(new Dictionary<string, object?>
        {
            ["label"] = new[] { "bug", "docs" }
        });

        var content = transport.Last.Content.ShouldBeOfType<FormContent>();

        content.Fields.Count(field => field.Key == "label").ShouldBe(2);
    }

    [Fact]
    public async Task A_null_form_value_is_sent_as_an_empty_field()
    {
        // Unlike a query parameter, where null means "omit". A form field that was named has to be
        // present: dropping it changes which fields the server sees as supplied at all.
        var (client, transport) = Build();

        await client.SubmitTypedAsync(new Dictionary<string, object?> { ["empty"] = null });

        var content = transport.Last.Content.ShouldBeOfType<FormContent>();

        content.Fields.ShouldContain(field => field.Key == "empty" && field.Value == string.Empty);
    }

    [Fact]
    public async Task Parts_become_a_multipart_body()
    {
        var (client, transport) = Build();

        await client.UploadAsync(
        [
            new MultipartPart("field", RequestContent.FromString("value")),
            new MultipartPart("file", RequestContent.FromBytes("data"u8.ToArray()), "data.bin")
        ]);

        var content = transport.Last.Content.ShouldBeOfType<MultipartFormContent>();

        content.ContentType.ShouldBe("multipart/form-data");
        content.Parts.Count.ShouldBe(2);
        content.Parts[1].FileName.ShouldBe("data.bin");

        Encoding.UTF8.GetString(content.Parts[0].Content
            .ShouldBeOfType<BinaryContent>().Bytes.Span).ShouldBe("value");
    }

    [Fact]
    public async Task A_multipart_request_requires_the_capability()
    {
        // Derived from the request itself, so the check is the same for every transport.
        var transport = new RecordingTransport(
            capabilities: TransportCapabilities.None,
            respond: _ => Responses.Json("""{"id":1,"name":"ok"}"""));

        var services = new ServiceCollection();
        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);
        services.AddRunaxClient<IFormClient>();

        var client = services.BuildServiceProvider().GetRequiredService<IFormClient>();

        var thrown = await Should.ThrowAsync<TransportCapabilityException>(
            () => client.UploadAsync([new MultipartPart("f", RequestContent.FromString("v"))]));

        thrown.Missing.ShouldBe(TransportCapabilities.Multipart);
        transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_form_request_needs_no_capability()
    {
        // Every transport can encode a form, so requiring one would be gatekeeping for its own sake.
        var transport = new RecordingTransport(
            capabilities: TransportCapabilities.None,
            respond: _ => Responses.Json("""{"id":1,"name":"ok"}"""));

        var services = new ServiceCollection();
        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);
        services.AddRunaxClient<IFormClient>();

        var client = services.BuildServiceProvider().GetRequiredService<IFormClient>();

        await client.SubmitAsync(new Dictionary<string, string> { ["a"] = "b" });

        transport.Requests.Count.ShouldBe(1);
    }
}
