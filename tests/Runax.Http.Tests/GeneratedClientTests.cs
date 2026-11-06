using System.Text;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// End-to-end: an annotated interface, the generated implementation, the runtime, and a transport
/// that records what it was handed.
/// </summary>
public class GeneratedClientTests
{
    [Fact]
    public async Task A_registered_contract_resolves_to_a_generated_implementation()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"runax-http","stars":1}"""));

        var repository = await harness.Client.GetAsync("runax-software", "runax-http");

        repository.Name.ShouldBe("runax-http");
        repository.Stars.ShouldBe(1);
    }

    [Fact]
    public async Task The_request_path_appends_to_the_base_address_path()
    {
        // The base address is https://api.example.com/v3 and the template starts with a slash.
        // Uri's own relative resolution would discard /v3 and request https://api.example.com/repos/...,
        // silently dropping the part of the address someone deliberately configured.
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"n","stars":0}"""));

        await harness.Client.GetAsync("runax-software", "runax-http");

        harness.Request.RequestUri.ToString()
            .ShouldBe("https://api.example.com/v3/repos/runax-software/runax-http");
    }

    [Fact]
    public async Task Path_values_are_escaped_as_a_single_segment()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"n","stars":0}"""));

        await harness.Client.GetAsync("owner/with/slashes", "name?with=query");

        // Without escaping, the slashes would add path segments and the ? would start a query
        // string, addressing something other than the resource the caller named.
        harness.Request.RequestUri.AbsolutePath
            .ShouldBe("/v3/repos/owner%2Fwith%2Fslashes/name%3Fwith%3Dquery");

        harness.Request.RequestUri.Query.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_values_are_named_formatted_and_ordered()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("[]"));

        await harness.Client.ListIssuesAsync("runax-software", "runax-http", "closed", 50);

        harness.Request.RequestUri.Query.ShouldBe("?state=closed&per_page=50");
    }

    [Fact]
    public async Task A_collection_query_value_becomes_one_repeated_key_per_item()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("[]"));

        await harness.Client.ListIssuesAsync(
            "runax-software", "runax-http", labels: ["bug", "needs triage"]);

        harness.Request.RequestUri.Query
            .ShouldBe("?state=open&per_page=30&labels=bug&labels=needs%20triage");
    }

    [Fact]
    public async Task A_null_query_value_is_omitted_rather_than_sent_empty()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("[]"));

        await harness.Client.ListIssuesAsync("runax-software", "runax-http", labels: null);

        // "labels=" would mean "match the empty label" to some APIs and "no filter" to others.
        // Omitting it says the one thing that cannot be misread.
        harness.Request.RequestUri.Query.ShouldNotContain("labels");
    }

    [Fact]
    public async Task Interface_level_headers_are_sent_with_every_request()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"n","stars":0}"""));

        await harness.Client.GetAsync("owner", "name");

        harness.Header("User-Agent").ShouldBe("runax-http-tests");
    }

    [Fact]
    public async Task Parameter_headers_are_formatted_the_same_way_as_query_values()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"n","stars":0}"""));
        var trace = Guid.Parse("8ec0d1b1-7b2a-4c2e-9f3a-0d1e2f3a4b5c");

        await harness.Client.GetWithHeaderAsync("Bearer token", trace);

        harness.Header("Authorization").ShouldBe("Bearer token");
        harness.Header("X-Trace-Id").ShouldBe("8ec0d1b1-7b2a-4c2e-9f3a-0d1e2f3a4b5c");
    }

    [Fact]
    public async Task An_accept_header_is_sent_so_a_negotiating_server_knows_what_we_parse()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"n","stars":0}"""));

        await harness.Client.GetAsync("owner", "name");

        harness.Header("Accept").ShouldBe("application/json");
    }

    [Fact]
    public async Task A_method_header_overrides_the_serializer_content_type()
    {
        var harness = ClientHarness.Create(_ => Responses.Empty());

        await harness.Client.PutReadmeAsync("owner", "name", "# Title");

        harness.Header("Content-Type").ShouldBe("text/markdown");
    }

    [Fact]
    public async Task A_body_parameter_is_serialized_with_its_static_type()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"number":1,"title":"t"}"""));

        await harness.Client.CreateIssueAsync("owner", "name", new NewIssue("Found a bug"));

        var content = harness.Request.Content.ShouldBeOfType<BinaryContent>();
        Encoding.UTF8.GetString(content.Bytes.ToArray()).ShouldBe("""{"title":"Found a bug","body":null}""");
        content.ContentType.ShouldBe("application/json");
    }

    [Fact]
    public async Task A_raw_body_is_sent_as_written()
    {
        var harness = ClientHarness.Create(_ => Responses.Empty());

        await harness.Client.PutReadmeAsync("owner", "name", "# Not JSON");

        var content = harness.Request.Content.ShouldBeOfType<BinaryContent>();
        Encoding.UTF8.GetString(content.Bytes.ToArray()).ShouldBe("# Not JSON");
    }

    [Fact]
    public async Task A_method_returning_Task_sends_the_request_and_ignores_the_body()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"ignored":true}"""));

        await harness.Client.DeleteAsync("owner", "name");

        harness.Request.Method.ShouldBe(RequestMethod.Delete);
    }

    [Fact]
    public async Task An_error_status_throws_with_everything_needed_to_diagnose_it()
    {
        var harness = ClientHarness.Create(_ => Responses.Error(422, """{"errors":["name is required"]}"""));

        var thrown = await Should.ThrowAsync<HttpApiException>(
            () => harness.Client.CreateIssueAsync("owner", "name", new NewIssue("")));

        thrown.StatusCode.ShouldBe(422);
        thrown.Method.ShouldBe(RequestMethod.Post);
        thrown.RequestUri!.ToString().ShouldBe("https://api.example.com/v3/repos/owner/name/issues");
        thrown.TransportName.ShouldBe("recording");

        // The response is disposed before this exception surfaces, so the body has to have been
        // read eagerly or it would be gone by the time anyone wanted it.
        // Shouldly's net8.0/net9.0 ShouldContain takes a non-nullable string, so the assertion
        // needs the null check even though the line above has already proved there is a body.
        thrown.ResponseContent.ShouldNotBeNull();
        thrown.ResponseContent!.ShouldContain("name is required");
        thrown.Message.ShouldContain("422");
    }

    [Fact]
    public async Task A_method_returning_ApiResponse_does_not_throw_on_an_error_status()
    {
        var harness = ClientHarness.Create(_ => Responses.Error(404));

        var response = await harness.Client.TryGetAsync("owner", "missing");

        response.IsSuccessStatusCode.ShouldBeFalse();
        response.StatusCode.ShouldBe(404);
        response.Content.ShouldBeNull();
        response.Error.ShouldNotBeNull();
        response.Error!.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task A_method_returning_ApiResponse_carries_the_body_on_success()
    {
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"found","stars":7}"""));

        var response = await harness.Client.TryGetAsync("owner", "found");

        response.IsSuccessStatusCode.ShouldBeTrue();
        response.EnsureSuccess().Stars.ShouldBe(7);
    }

    [Fact]
    public async Task A_bodyless_ApiResponse_reports_the_status_without_deserializing()
    {
        var harness = ClientHarness.Create(_ => Responses.Empty(200));

        var response = await harness.Client.ExistsAsync("owner", "name");

        response.StatusCode.ShouldBe(200);
        harness.Request.Method.ShouldBe(RequestMethod.Head);
    }

    [Fact]
    public async Task A_204_yields_no_content_rather_than_a_parse_error()
    {
        var harness = ClientHarness.Create(_ => Responses.Empty(204));

        var response = await harness.Client.TryGetAsync("owner", "name");

        response.StatusCode.ShouldBe(204);
        response.Content.ShouldBeNull();
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_transport()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = ClientHarness.Create(_ => Responses.Json("""{"name":"n","stars":0}"""));

        await harness.Client.GetAsync("owner", "name", cancellation.Token);

        // A client that quietly drops the token looks identical until something needs to time out.
        harness.Transport.LastToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task A_method_without_a_cancellation_token_still_works()
    {
        var harness = ClientHarness.Create(_ => Responses.Empty());

        await harness.Client.PutReadmeAsync("owner", "name", "# Title");

        harness.Transport.LastToken.ShouldBe(CancellationToken.None);
    }
}
