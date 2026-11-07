using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;

namespace Runax.Http.Tests;

/// <summary>
/// How a query key is derived from an identifier.
/// </summary>
/// <remarks>
/// <para>
/// The policy is applied by the generator, so every key in the emitted code is a literal and
/// nothing is transformed per request. That is why a policy is free at run time, and why
/// <c>AsDeclared</c> is not a performance choice.
/// </para>
/// <para>
/// camelCase is the default because the body serializer already does that to the same type: a
/// filter sent as <c>[Body]</c> serializes to <c>{"perPage":…}</c>, so sending <c>?PerPage=</c>
/// for the identical type would have one library spelling one type two ways.
/// </para>
/// </remarks>
public class QueryNamingTests
{
    private static (TContract Client, RecordingTransport Transport) Build<TContract>()
        where TContract : class
    {
        var transport = new RecordingTransport(respond: _ => Responses.Json("""{"id":1,"name":"w"}"""));
        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        services.AddRunaxClient<TContract>();

        return (services.BuildServiceProvider().GetRequiredService<TContract>(), transport);
    }

    private static string Query(RecordingTransport transport)
        => Uri.UnescapeDataString(transport.Last.RequestUri.Query);

    private static ReportFilter Filter() => new()
    {
        State = "open",
        PerPage = 50,
        HTTPReferer = "ref",
        Top10Items = 7,
        Exact = "kept"
    };

    [Fact]
    public async Task The_default_is_camelCase()
    {
        var (client, transport) = Build<IDefaultNamingClient>();

        await client.ListAsync(Filter());

        Query(transport).ShouldBe("?state=open&perPage=50&httpReferer=ref&top10Items=7&EXACT=kept");
    }

    [Fact]
    public async Task An_interface_policy_applies_to_properties_and_parameters_alike()
    {
        // Both, deliberately. A policy that transformed object fields but left parameter names
        // alone would leave half the query string in the other convention — which is worse than
        // either consistent choice, and precisely the trap someone setting snake_case would fall
        // into.
        var (client, transport) = Build<ISnakeCaseClient>();

        await client.ListAsync(Filter(), 3);

        Query(transport)
            .ShouldBe("?state=open&per_page=50&http_referer=ref&top_10_items=7&EXACT=kept&page_number=3");
    }

    [Fact]
    public async Task An_acronym_run_stays_one_word()
    {
        // h_t_t_p_referer is what a naive "uppercase starts a word" split produces, and nobody
        // writing an API spells it that way.
        var (client, transport) = Build<ISnakeCaseClient>();

        await client.ListAsync(Filter(), 1);

        Query(transport).ShouldContain("http_referer=ref", Case.Sensitive);
    }

    [Fact]
    public async Task A_digit_run_is_its_own_word()
    {
        var (client, transport) = Build<ISnakeCaseClient>();

        await client.ListAsync(Filter(), 1);

        Query(transport).ShouldContain("top_10_items=7", Case.Sensitive);
    }

    [Fact]
    public async Task An_explicit_name_survives_every_policy()
    {
        // [Query("EXACT")] is the author stating the key outright. Transforming it would leave no
        // way to say "this one, exactly".
        var (snake, snakeTransport) = Build<ISnakeCaseClient>();
        await snake.ListAsync(Filter(), 1);
        Query(snakeTransport).ShouldContain("EXACT=kept", Case.Sensitive);

        var (camel, camelTransport) = Build<IDefaultNamingClient>();
        await camel.ListAsync(Filter());
        Query(camelTransport).ShouldContain("EXACT=kept", Case.Sensitive);
    }

    [Fact]
    public async Task A_parameter_can_override_its_client_policy()
    {
        // For the one endpoint whose convention differs from the rest of its API.
        var (client, transport) = Build<ISnakeCaseClient>();

        await client.LegacyAsync(Filter());

        Query(transport).ShouldBe("?State=open&PerPage=50&HTTPReferer=ref&Top10Items=7&EXACT=kept");
    }

    [Fact]
    public async Task AsDeclared_is_how_the_previous_behaviour_is_kept()
    {
        // 1.0 sent parameter names verbatim. Anyone relying on a non-camelCase parameter name has
        // this as a one-attribute escape rather than a rename.
        var (client, transport) = Build<ISnakeCaseClient>();

        await client.LegacyAsync(Filter());

        Query(transport).ShouldContain("HTTPReferer=ref", Case.Sensitive);
    }
}
