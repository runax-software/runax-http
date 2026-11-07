using Runax.Http.Retry;

namespace Runax.Http.Tests;

/// <summary>
/// The backoff curve, the jitter bounds and the <c>Retry-After</c> handling, tested directly
/// rather than through a loop that would have to actually wait.
/// </summary>
public class RetryPlannerTests
{
    private static RetryOptions Options(Action<RetryOptions>? configure = null)
    {
        var options = new RetryOptions
        {
            MaxAttempts = 5,
            Delay = TimeSpan.FromSeconds(1),
            UseJitter = false
        };

        configure?.Invoke(options);

        return options;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void Exponential_backoff_doubles(int completedAttempts, int expectedSeconds)
        => RetryPlanner.ComputeDelay(Options(), completedAttempts, null)
            .ShouldBe(TimeSpan.FromSeconds(expectedSeconds));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void Linear_backoff_adds(int completedAttempts, int expectedSeconds)
        => RetryPlanner.ComputeDelay(
                Options(o => o.Backoff = BackoffStrategy.Linear), completedAttempts, null)
            .ShouldBe(TimeSpan.FromSeconds(expectedSeconds));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Constant_backoff_does_not_grow(int completedAttempts)
        => RetryPlanner.ComputeDelay(
                Options(o => o.Backoff = BackoffStrategy.Constant), completedAttempts, null)
            .ShouldBe(TimeSpan.FromSeconds(1));

    [Fact]
    public void The_delay_is_capped()
    {
        // Without a ceiling, exponential backoff over enough attempts becomes a wait nobody
        // intended — eight attempts from one second is over two minutes.
        var options = Options(o => o.MaxDelay = TimeSpan.FromSeconds(5));

        RetryPlanner.ComputeDelay(options, 10, null).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Jitter_stays_within_half_the_computed_delay()
    {
        var options = Options(o => o.UseJitter = true);

        for (var i = 0; i < 200; i++)
        {
            var delay = RetryPlanner.ComputeDelay(options, 3, null);

            // Full jitter over [delay/2, delay]: keeps most of the backoff while breaking up the
            // synchronized wave of clients that all failed at the same instant.
            delay.ShouldNotBeNull();
            delay!.Value.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
            delay.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(4));
        }
    }

    [Fact]
    public void Retry_After_overrides_the_curve()
    {
        // The server named a number. Second-guessing it on a 429 is how a rate limit becomes a ban.
        RetryPlanner.ComputeDelay(Options(), 1, TimeSpan.FromSeconds(7))
            .ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Retry_After_can_be_ignored()
        => RetryPlanner.ComputeDelay(
                Options(o => o.RespectRetryAfter = false), 1, TimeSpan.FromSeconds(7))
            .ShouldBe(TimeSpan.FromSeconds(1));

    [Fact]
    public void A_Retry_After_beyond_the_limit_means_do_not_retry()
        => RetryPlanner.ComputeDelay(
                Options(o => o.MaxRetryAfterDelay = TimeSpan.FromSeconds(5)),
                1,
                TimeSpan.FromHours(1))
            .ShouldBeNull();

    [Fact]
    public void A_negative_Retry_After_becomes_no_wait()
        => RetryPlanner.ComputeDelay(Options(), 1, TimeSpan.FromSeconds(-5))
            .ShouldBe(TimeSpan.Zero);

    [Fact]
    public void Retry_After_reads_delta_seconds()
        => RetryPlanner.ReadRetryAfter(
                [new KeyValuePair<string, string>("Retry-After", "120")],
                DateTimeOffset.UnixEpoch)
            .ShouldBe(TimeSpan.FromMinutes(2));

    [Fact]
    public void Retry_After_reads_an_http_date()
    {
        // The date form is as common in the wild as delta-seconds and useless unless parsed.
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        RetryPlanner.ReadRetryAfter(
                [new KeyValuePair<string, string>("Retry-After", "Sat, 07 Nov 2026 12:00:30 GMT")],
                now)
            .ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_date_already_in_the_past_becomes_no_wait()
        => RetryPlanner.ReadRetryAfter(
                [new KeyValuePair<string, string>("Retry-After", "Sat, 07 Nov 2026 11:00:00 GMT")],
                new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero))
            .ShouldBe(TimeSpan.Zero);

    [Fact]
    public void An_unparseable_Retry_After_is_ignored_rather_than_guessed_at()
        => RetryPlanner.ReadRetryAfter(
                [new KeyValuePair<string, string>("Retry-After", "soon")],
                DateTimeOffset.UnixEpoch)
            .ShouldBeNull();

    [Fact]
    public void A_missing_Retry_After_is_null()
        => RetryPlanner.ReadRetryAfter([], DateTimeOffset.UnixEpoch).ShouldBeNull();

    [Fact]
    public void The_header_is_matched_case_insensitively()
        => RetryPlanner.ReadRetryAfter(
                [new KeyValuePair<string, string>("retry-after", "5")],
                DateTimeOffset.UnixEpoch)
            .ShouldBe(TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData(RequestMethod.Get, true)]
    [InlineData(RequestMethod.Head, true)]
    [InlineData(RequestMethod.Options, true)]
    [InlineData(RequestMethod.Put, true)]
    [InlineData(RequestMethod.Delete, true)]
    [InlineData(RequestMethod.Post, false)]
    [InlineData(RequestMethod.Patch, false)]
    public void Only_idempotent_methods_are_allowed_by_default(RequestMethod method, bool allowed)
        => Options().AllowsMethod(method).ShouldBe(allowed);

    [Theory]
    [InlineData(RequestMethod.Post)]
    [InlineData(RequestMethod.Patch)]
    public void Non_idempotent_methods_can_be_opted_in(RequestMethod method)
        => Options(o => o.RetryNonIdempotentMethods = true).AllowsMethod(method).ShouldBeTrue();

    [Fact]
    public void One_attempt_means_retrying_is_off()
        => new RetryOptions().IsEnabled.ShouldBeFalse();
}
