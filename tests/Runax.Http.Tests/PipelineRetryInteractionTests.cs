using Microsoft.Extensions.DependencyInjection;
using Runax.Http.DependencyInjection;
using Runax.Http.Tests.Contracts;
using Runax.Http.Tests.Fakes;
using Runax.Http.Transports;

namespace Runax.Http.Tests;

/// <summary>
/// The pipeline and per-method <c>[Retry]</c> together.
/// </summary>
/// <remarks>
/// Written after the two features were developed in parallel, because this is the behaviour neither
/// of them could test on its own: the pipeline's "stages re-run per attempt" claim and
/// <c>[Retry]</c>'s "a declaration turns retrying on by itself" claim only meet here. Each feature
/// passing its own suite says nothing about the pair.
/// </remarks>
public class PipelineRetryInteractionTests
{
    private static (IFlakyClient Client, RecordingTransport Transport) Build(
        Func<string?> token,
        Action<IRunaxClientBuilder<IFlakyClient>>? configure = null)
    {
        var call = 0;

        var transport = new RecordingTransport(respond: _ =>
            call++ == 0
                ? Responses.Error(503)
                : Responses.Json("""{"name":"recovered","stars":1}"""));

        var services = new ServiceCollection();

        var shared = services.AddRunaxHttp();
        shared.UseSystemTextJson();
        shared.AddTransport(transport);
        shared.UseDefaultTransport(transport.Name);

        var client = services.AddRunaxClient<IFlakyClient>();
        client.UseBearerToken(token);
        configure?.Invoke(client);

        return (services.BuildServiceProvider().GetRequiredService<IFlakyClient>(), transport);
    }

    private static string? Authorization(TransportRequest request)
    {
        foreach (var (name, value) in request.Headers)
        {
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    [Fact]
    public async Task A_declaration_alone_drives_the_retry_and_the_pipeline_still_runs_per_attempt()
    {
        // No WithRetry anywhere: [Retry(3)] on the method is the only thing enabling retries, and
        // the auth stage still has to be asked again for the second attempt.
        var issued = 0;

        var (client, transport) = Build(() => $"token-{++issued}");

        var repository = await client.GetSlowReportAsync(1);

        repository.Name.ShouldBe("recovered");
        transport.Requests.Count.ShouldBe(2);

        // The refreshed token reached the retry. If the pipeline ran outside the loop, both
        // attempts would carry token-1 and a token that expired mid-call could never recover.
        Authorization(transport.Requests[1]).ShouldBe("Bearer token-2");
    }

    [Fact]
    public async Task The_first_attempt_is_not_rewritten_by_the_second()
    {
        // The regression the pipeline work turned up: the context handed out its live header list,
        // so the next attempt's token overwrote a request already sent. It showed up as attempt one
        // appearing to carry attempt two's token — invisible unless something looks back at it.
        var issued = 0;

        var (client, transport) = Build(() => $"token-{++issued}");

        await client.GetSlowReportAsync(1);

        Authorization(transport.Requests[0]).ShouldBe("Bearer token-1");
        Authorization(transport.Requests[1]).ShouldBe("Bearer token-2");
    }

    [Fact]
    public async Task A_correlation_id_stays_put_across_an_attribute_driven_retry()
    {
        // The other half of the pipeline's positioning decision: stages re-run, but a correlation
        // id must not change between tries of one call — ids that differ cannot correlate them,
        // which is the only thing an id is for.
        var (client, transport) = Build(() => "token", c => c.UseCorrelation());

        await client.GetSlowReportAsync(1);

        transport.Requests.Count.ShouldBe(2);

        var first = transport.Requests[0].Headers
            .First(h => h.Key.Contains("Correlation", StringComparison.OrdinalIgnoreCase)).Value;

        var second = transport.Requests[1].Headers
            .First(h => h.Key.Contains("Correlation", StringComparison.OrdinalIgnoreCase)).Value;

        first.ShouldBe(second);
        first.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WithoutRetries_beats_a_declaration_even_with_a_pipeline_attached()
    {
        // The kill switch has to survive the pipeline being in the way. An operator reaching for
        // WithoutRetries() mid-incident is not going to check whether middleware changes the answer.
        var (client, transport) = Build(() => "token", c => c.WithoutRetries());

        await Should.ThrowAsync<HttpApiException>(() => client.GetSlowReportAsync(1));

        transport.Requests.Count.ShouldBe(1);
    }
}
