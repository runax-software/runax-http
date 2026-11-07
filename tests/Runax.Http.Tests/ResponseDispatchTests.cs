using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// Choosing the response type at run time: by status code, and — for an API that answers 200
/// whatever happened — by what the body turned out to contain.
/// </summary>
public class ResponseDispatchTests
{
    private static (IQuirkyClient Client, RecordingTransport Transport) Build(
        Func<TransportRequest, TransportResponse> respond)
    {
        var transport = new RecordingTransport(respond: respond);
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        services.AddRunaxClient<IQuirkyClient>();

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IQuirkyClient>(), transport);
    }

    [Fact]
    public async Task A_status_code_selects_the_success_shape()
    {
        var (client, _) = Build(_ => Responses.Json("""{"id":1,"name":"widget"}"""));

        var result = await client.GetAsync(1);

        result.Matched.ShouldBeTrue();
        result.Require<Widget>().Name.ShouldBe("widget");
    }

    [Fact]
    public async Task A_status_code_selects_an_error_shape()
    {
        var (client, _) = Build(_ => Responses.Json("""{"message":"no such widget"}""", 404));

        var result = await client.GetAsync(1);

        result.StatusCode.ShouldBe(404);
        result.IsSuccessStatusCode.ShouldBeFalse();
        result.Is<NotFound>().ShouldBeTrue();
        result.Require<NotFound>().Message.ShouldBe("no such widget");
    }

    [Fact]
    public async Task Each_declared_status_gets_its_own_type()
    {
        var (client, _) = Build(_ => Responses.Json("""{"errors":["name is required"]}""", 422));

        var result = await client.GetAsync(1);

        result.TryGet<ValidationProblem>(out var problem).ShouldBeTrue();
        problem!.Errors.ShouldBe(["name is required"]);
    }

    [Fact]
    public async Task The_value_pattern_matches_without_a_cast()
    {
        // The reason Value is typed as object: C# has no union, and a pattern match is how the
        // static typing comes back. This is the shape callers are expected to write.
        var (client, _) = Build(_ => Responses.Json("""{"message":"gone"}""", 404));

        var result = await client.GetAsync(1);

        var described = result.Value switch
        {
            Widget widget => $"widget {widget.Name}",
            NotFound missing => $"missing: {missing.Message}",
            ValidationProblem => "invalid",
            _ => "unrecognized"
        };

        described.ShouldBe("missing: gone");
    }

    [Fact]
    public async Task A_status_range_matches_a_whole_class_of_codes()
    {
        var (client, _) = Build(_ => Responses.Json("""{"error":"boom","code":9}""", 503));

        var result = await client.GetHistoryAsync(1);

        result.Require<ApiError>().Error.ShouldBe("boom");
    }

    [Fact]
    public async Task An_always_200_api_is_discriminated_on_the_body()
    {
        // The case a status code cannot express: the API answered 200 and put the failure in the
        // body. Without this, the client would happily deserialize an error into a Widget and the
        // caller would get a Widget with default fields.
        var (client, _) = Build(_ => Responses.Json("""{"error":"not found","code":404}"""));

        var result = await client.GetLegacyAsync(1);

        result.StatusCode.ShouldBe(200);
        result.IsSuccessStatusCode.ShouldBeTrue();
        result.Require<ApiError>().Code.ShouldBe(404);
    }

    [Fact]
    public async Task The_same_endpoint_still_reads_a_success_body()
    {
        var (client, _) = Build(_ => Responses.Json("""{"id":7,"name":"legacy"}"""));

        var result = await client.GetLegacyAsync(1);

        result.Require<Widget>().Id.ShouldBe(7);
    }

    [Fact]
    public async Task A_boolean_flag_in_the_body_can_be_the_discriminator()
    {
        // "success": false compares against the JSON spelling, which is what someone writing
        // WhenJsonPropertyEquals = "false" expects.
        var (client, _) = Build(_ => Responses.Json("""{"success":false,"error":"nope","code":1}"""));

        var result = await client.GetEnvelopeAsync(1);

        result.Require<ApiError>().Error.ShouldBe("nope");
    }

    [Fact]
    public async Task The_flag_being_true_falls_through_to_the_success_shape()
    {
        var (client, _) = Build(_ => Responses.Json("""{"success":true,"data":{"id":3,"name":"w"}}"""));

        var result = await client.GetEnvelopeAsync(1);

        var envelope = result.Require<Envelope>();
        envelope.Success.ShouldBeTrue();
        envelope.Data!.Id.ShouldBe(3);
    }

    [Fact]
    public async Task A_header_can_be_the_discriminator()
    {
        var (client, _) = Build(_ => new TransportResponse(200)
        {
            Headers = [new KeyValuePair<string, string>("X-Error", "1")],
            Content = new MemoryStream("""{"error":"via header","code":2}"""u8.ToArray())
        });

        var result = await client.GetByHeaderAsync(1);

        result.Require<ApiError>().Error.ShouldBe("via header");
    }

    [Fact]
    public async Task A_missing_header_falls_through()
    {
        var (client, _) = Build(_ => Responses.Json("""{"id":4,"name":"ok"}"""));

        var result = await client.GetByHeaderAsync(1);

        result.Require<Widget>().Id.ShouldBe(4);
    }

    [Fact]
    public async Task Conditions_on_one_attribute_are_combined_with_and()
    {
        // [Response(200, typeof(ApiError), WhenJsonProperty = "error")] needs both. A 200 without
        // the property has to fall through to the next declaration.
        var (client, _) = Build(_ => Responses.Json("""{"error":"both","code":1}"""));

        (await client.GetCombinedAsync(1)).Is<ApiError>().ShouldBeTrue();

        var (plain, _) = Build(_ => Responses.Json("""{"id":5,"name":"plain"}"""));

        (await plain.GetCombinedAsync(1)).Is<Widget>().ShouldBeTrue();
    }

    [Fact]
    public async Task An_undeclared_response_comes_back_unmatched_with_the_body_kept()
    {
        var (client, _) = Build(_ => Responses.Json("""{"unexpected":true}""", 418));

        var result = await client.GetStrictAsync(1);

        // Not guessed at. The body is the only clue as to what the API actually sent, and
        // therefore what [Response] is missing.
        result.Matched.ShouldBeFalse();
        result.Value.ShouldBeNull();
        result.ValueType.ShouldBeNull();
        result.StatusCode.ShouldBe(418);
        result.UnmatchedContent.ShouldBe("""{"unexpected":true}""");
    }

    [Fact]
    public async Task Require_on_an_unmatched_response_explains_itself()
    {
        var (client, _) = Build(_ => Responses.Json("""{"unexpected":true}""", 418));

        var result = await client.GetStrictAsync(1);

        var thrown = Should.Throw<HttpApiException>(() => result.Require<Widget>());

        thrown.StatusCode.ShouldBe(418);
        thrown.Message.ShouldContain("no declared response matched");
        thrown.ResponseContent.ShouldNotBeNull();
        thrown.ResponseContent!.ShouldContain("unexpected");
    }

    [Fact]
    public async Task Require_for_the_wrong_declared_type_names_what_it_actually_was()
    {
        var (client, _) = Build(_ => Responses.Json("""{"message":"gone"}""", 404));

        var result = await client.GetAsync(1);

        var thrown = Should.Throw<HttpApiException>(() => result.Require<Widget>());

        thrown.Message.ShouldContain(nameof(NotFound));
    }

    [Fact]
    public async Task An_error_status_does_not_throw_on_this_path()
    {
        // A method declaring ApiResult has said an error status is one of the expected answers.
        var (client, _) = Build(_ => Responses.Json("""{"message":"gone"}""", 404));

        await Should.NotThrowAsync(() => client.GetAsync(1));
    }

    [Fact]
    public async Task A_matched_response_with_an_empty_body_still_counts_as_matched()
    {
        // A 200 with no body is distinguishable from a response nothing matched: ValueType is set
        // either way, Value is null only in the first case.
        var (client, _) = Build(_ => Responses.Empty(200));

        var result = await client.GetStrictAsync(1);

        result.Matched.ShouldBeTrue();
        result.ValueType.ShouldBe(typeof(Widget));
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task A_non_json_body_does_not_fail_the_discriminator()
    {
        // An API that answers 200 with an HTML error page should fall through to the next
        // declaration, not blow up with a parse error from the condition check itself.
        var (client, _) = Build(_ => new TransportResponse(200)
        {
            Content = new MemoryStream("<html>down for maintenance</html>"u8.ToArray())
        });

        // Falls past the "error" property test, then fails to deserialize as a Widget — which is
        // a deserialization error, not a condition-evaluation error.
        await Should.ThrowAsync<System.Text.Json.JsonException>(() => client.GetLegacyAsync(1));
    }

    [Fact]
    public async Task The_result_carries_the_status_headers_and_transport()
    {
        var (client, _) = Build(_ => new TransportResponse(404)
        {
            ReasonPhrase = "Not Found",
            Headers = [new KeyValuePair<string, string>("X-Request-Id", "abc")],
            Content = new MemoryStream("""{"message":"gone"}"""u8.ToArray())
        });

        var result = await client.GetAsync(1);

        result.ReasonPhrase.ShouldBe("Not Found");
        result.TransportName.ShouldBe("recording");
        result.Headers.ShouldContain(header => header.Key == "X-Request-Id");
    }
}
