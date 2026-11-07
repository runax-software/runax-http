using Runax.Http.Testing;
using Runax.Http.Transports;

namespace Runax.Http.TestKit.Tests;

/// <summary>
/// The TestKit, used the way a consumer would use it.
/// </summary>
/// <remarks>
/// Doubles as the worked example the docs point at: if the ergonomics are bad, it shows up here
/// first.
/// </remarks>
public class RunaxClientHarnessTests
{
    [Fact]
    public async Task A_harness_stands_up_a_real_client_over_a_recording_transport()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();
        harness.Transport.RespondsJson("""{"name":"runax-http","stars":1}""");

        var repository = await harness.Client.GetAsync("runax-software", "runax-http");

        repository.Name.ShouldBe("runax-http");
        repository.Stars.ShouldBe(1);
    }

    [Fact]
    public async Task The_recorded_request_is_what_the_client_actually_built()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();
        harness.Transport.RespondsJson("""{"name":"n","stars":0}""");

        await harness.Client.GetAsync("runax-software", "runax-http");

        harness.Requests.ShouldHaveSingleItem();
        harness.Last.Method.ShouldBe(RequestMethod.Get);
        harness.Last.Path.ShouldBe("/repos/runax-software/runax-http");
    }

    [Fact]
    public async Task A_contract_with_no_base_address_still_works()
    {
        // IRepositoryClient declares none on purpose. Without the harness supplying one, every
        // test would have to — which is friction for something no test cares about.
        using var harness = RunaxClientHarness.For<IRepositoryClient>();
        harness.Transport.RespondsJson("""{"name":"n","stars":0}""");

        await harness.Client.GetAsync("o", "n");

        harness.Last.Uri.Host.ShouldBe("runax.test");
    }

    [Fact]
    public async Task A_caller_supplied_base_address_wins()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>(
            client => client.WithBaseAddress("https://api.example.com/v3"));

        harness.Transport.RespondsJson("""{"name":"n","stars":0}""");

        await harness.Client.GetAsync("o", "n");

        harness.Last.Uri.ToString().ShouldBe("https://api.example.com/v3/repos/o/n");
    }

    [Fact]
    public async Task Query_values_are_readable_by_name()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();
        harness.Transport.RespondsJson("[]");

        await harness.Client.ListIssuesAsync("o", "n", "closed", ["bug", "needs triage"]);

        harness.Last.Query("state").ShouldBe("closed");

        // A collection argument expands to repeated keys, so reading one value would silently lose
        // the rest — which is why QueryValues exists.
        harness.Last.QueryValues("labels").ShouldBe(["bug", "needs triage"]);
    }

    [Fact]
    public async Task Headers_are_readable_by_name()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();
        harness.Transport.RespondsJson("""{"number":1,"title":"t"}""");

        await harness.Client.CreateIssueAsync("o", "n", new NewIssue("found a bug"), "Bearer token");

        harness.Last.Header("Authorization").ShouldBe("Bearer token");
        harness.Last.Header("Accept").ShouldBe("application/json");
        harness.Last.Header("X-Not-Sent").ShouldBeNull();
    }

    [Fact]
    public async Task The_body_is_readable_as_text()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();
        harness.Transport.RespondsJson("""{"number":1,"title":"t"}""");

        await harness.Client.CreateIssueAsync("o", "n", new NewIssue("found a bug"), "Bearer token");

        // The point of the TestKit: asserting on the serialized body without a server, and without
        // mocking anything that would skip the serialization under test.
        harness.Last.Body.ShouldBe("""{"title":"found a bug"}""");
    }

    [Fact]
    public async Task Responses_are_used_in_the_order_they_were_queued()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();

        harness.Transport
            .RespondsJson("""{"name":"first","stars":1}""")
            .RespondsJson("""{"name":"second","stars":2}""");

        (await harness.Client.GetAsync("o", "n")).Name.ShouldBe("first");
        (await harness.Client.GetAsync("o", "n")).Name.ShouldBe("second");

        harness.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_response_can_be_computed_from_the_request()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();

        harness.Transport.AlwaysResponds(request =>
            TestResponse.Json($$"""{"name":"{{request.Path.Split('/')[^1]}}","stars":0}"""));

        (await harness.Client.GetAsync("owner", "echoed")).Name.ShouldBe("echoed");
    }

    [Fact]
    public async Task An_error_status_behaves_as_it_would_in_production()
    {
        using var harness = RunaxClientHarness.Responding<IRepositoryClient>(
            TestResponse.Error(404, """{"message":"gone"}"""));

        var thrown = await Should.ThrowAsync<HttpApiException>(
            () => harness.Client.GetAsync("o", "missing"));

        thrown.StatusCode.ShouldBe(404);
        thrown.ResponseContent.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_non_throwing_return_type_behaves_too()
    {
        using var harness = RunaxClientHarness.Responding<IRepositoryClient>(
            TestResponse.Status(404));

        var response = await harness.Client.TryGetAsync("o", "missing");

        response.IsSuccessStatusCode.ShouldBeFalse();
        response.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_transport_failure_can_be_simulated()
    {
        // Needed for testing retry behaviour: a status code cannot express "the server was never
        // reached", and that is the case retrying actually exists for.
        using var harness = RunaxClientHarness.For<IRepositoryClient>();

        harness.Transport
            .FailsOnce()
            .RespondsJson("""{"name":"recovered","stars":1}""");

        await Should.ThrowAsync<TransportException>(() => harness.Client.GetAsync("o", "n"));

        (await harness.Client.GetAsync("o", "n")).Name.ShouldBe("recovered");
    }

    [Fact]
    public async Task Retries_can_be_tested_through_the_harness()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>(
            client => client.WithRetry(options =>
            {
                options.MaxAttempts = 3;
                options.Delay = TimeSpan.Zero;
            }));

        harness.Transport
            .RespondsStatus(503)
            .RespondsJson("""{"name":"second-try","stars":1}""");

        (await harness.Client.GetAsync("o", "n")).Name.ShouldBe("second-try");

        harness.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_cancellation_token_is_recorded()
    {
        using var cancellation = new CancellationTokenSource();
        using var harness = RunaxClientHarness.Responding<IRepositoryClient>(
            TestResponse.Json("""{"name":"n","stars":0}"""));

        await harness.Client.GetAsync("o", "n", cancellation.Token);

        harness.Transport.LastToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task A_method_returning_Task_records_its_request()
    {
        using var harness = RunaxClientHarness.Responding<IRepositoryClient>(TestResponse.Status(204));

        await harness.Client.DeleteAsync("o", "n");

        harness.Last.Method.ShouldBe(RequestMethod.Delete);
        harness.Last.Body.ShouldBeNull();
    }

    [Fact]
    public void An_unscripted_request_says_so_rather_than_failing_obscurely()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();

        var thrown = Should.Throw<InvalidOperationException>(
            () => harness.Client.GetAsync("o", "n").GetAwaiter().GetResult());

        thrown.Message.ShouldContain("no response scripted");
        thrown.Message.ShouldContain("Responds");
    }

    [Fact]
    public void Reading_Last_before_anything_was_sent_explains_itself()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>();

        Should.Throw<InvalidOperationException>(() => harness.Last)
            .Message.ShouldContain("No request has been sent");
    }

    [Fact]
    public void The_test_transport_claims_everything_by_default()
    {
        // So the capability check never fails a test that was about something else.
        new TestTransport().Capabilities.ShouldBe(TestTransport.Everything);
    }

    [Fact]
    public async Task The_capability_check_can_still_be_tested_by_narrowing_it()
    {
        using var harness = RunaxClientHarness.For<IRepositoryClient>(
            client => client.WithTimeout(TimeSpan.FromSeconds(5)),
            new TestTransport(capabilities: TransportCapabilities.None));

        var thrown = await Should.ThrowAsync<TransportCapabilityException>(
            () => harness.Client.GetAsync("o", "n"));

        thrown.Missing.ShouldBe(TransportCapabilities.PerRequestTimeout);
    }
}
