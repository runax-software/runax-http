using System.Globalization;
using Runax.Http.Transports;

namespace Runax.Http.Retry;

/// <summary>
/// Decides whether a request may be retried and how long to wait first.
/// </summary>
/// <remarks>
/// Kept as pure functions so the interesting rules — the backoff curve, the jitter bounds, the
/// <c>Retry-After</c> handling — can be tested without a clock or a network.
/// </remarks>
internal static class RetryPlanner
{
    /// <summary>
    /// Whether the request can be sent more than once at all, independent of any outcome.
    /// </summary>
    internal static bool IsRetryable(RetryOptions options, TransportRequest request)
    {
        if (!options.IsEnabled || !options.AllowsMethod(request.Method))
            return false;

        // A streamed body has already been read by the first attempt. Sending it again would send
        // a truncated request, and buffering it instead would mean holding an upload of unknown
        // size in memory on the off chance of a retry.
        return request.Content is not StreamedContent;
    }

    /// <summary>Whether this status code is worth another attempt.</summary>
    internal static bool ShouldRetryStatus(RetryOptions options, int statusCode)
        => options.RetryableStatusCodes.Contains(statusCode);

    /// <summary>
    /// How long to wait before attempt <paramref name="completedAttempts"/> + 1.
    /// </summary>
    /// <param name="options">The retry settings.</param>
    /// <param name="completedAttempts">How many attempts have already been made, starting at 1.</param>
    /// <param name="retryAfter">A <c>Retry-After</c> value from the response, if there was one.</param>
    /// <returns>
    /// The wait, or <see langword="null"/> if the server asked for longer than
    /// <see cref="RetryOptions.MaxRetryAfterDelay"/> — which is a refusal, not an instruction to
    /// block for it.
    /// </returns>
    internal static TimeSpan? ComputeDelay(
        RetryOptions options,
        int completedAttempts,
        TimeSpan? retryAfter)
    {
        if (options.RespectRetryAfter && retryAfter is { } requested)
        {
            if (requested > options.MaxRetryAfterDelay)
                return null;

            // Taken as given rather than jittered or capped by MaxDelay: the server named a
            // number, and second-guessing it on a 429 is how a rate limit becomes a ban.
            return requested < TimeSpan.Zero ? TimeSpan.Zero : requested;
        }

        var scale = options.Backoff switch
        {
            BackoffStrategy.Constant => 1.0,
            BackoffStrategy.Linear => completedAttempts,
            _ => Math.Pow(2, completedAttempts - 1)
        };

        var delay = options.Delay * scale;

        if (delay > options.MaxDelay)
            delay = options.MaxDelay;

        if (!options.UseJitter || delay <= TimeSpan.Zero)
            return delay;

        // Full jitter: a random point in [delay/2, delay]. Keeps most of the backoff while
        // breaking up the synchronized wave of clients that all failed at the same instant.
        var half = delay / 2;

        return half + half * Random.Shared.NextDouble();
    }

    /// <summary>
    /// Reads <c>Retry-After</c>, which is either a number of seconds or an HTTP date.
    /// </summary>
    /// <param name="headers">The response headers.</param>
    /// <param name="now">The current time, for the date form.</param>
    /// <returns>The requested wait, or <see langword="null"/> if absent or unparseable.</returns>
    internal static TimeSpan? ReadRetryAfter(
        IReadOnlyList<KeyValuePair<string, string>> headers,
        DateTimeOffset now)
    {
        foreach (var (name, value) in headers)
        {
            if (!string.Equals(name, "Retry-After", StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);

            // The other legal form is an HTTP date, which is as common in the wild as the
            // delta-seconds form and is useless unless parsed.
            if (DateTimeOffset.TryParse(
                    value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var when))
            {
                var wait = when - now;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }

            return null;
        }

        return null;
    }
}
