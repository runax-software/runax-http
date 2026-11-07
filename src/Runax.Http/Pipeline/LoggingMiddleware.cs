using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Runax.Http.Transports;

namespace Runax.Http.Pipeline;

/// <summary>
/// Logs each attempt: what was sent, what came back, and how long it took.
/// </summary>
/// <remarks>
/// Per attempt rather than per call, so a retried request produces a line per try with
/// <see cref="RequestContext.Attempt"/> telling them apart. A single line covering three attempts
/// would hide exactly the thing you opened the log to find.
/// </remarks>
public sealed class LoggingMiddleware : IHttpMiddleware
{
    private readonly ILogger _logger;
    private readonly HttpLoggingOptions _options;

    /// <summary>Initializes a new instance of the <see cref="LoggingMiddleware"/> class.</summary>
    /// <param name="logger">Where lines are written.</param>
    /// <param name="options">What to write. Defaults are safe; see <see cref="HttpLoggingOptions"/>.</param>
    public LoggingMiddleware(ILogger logger, HttpLoggingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _options = options ?? new HttpLoggingOptions();
    }

    /// <inheritdoc />
    public int Order => HttpMiddlewareOrder.Logging;

    /// <inheritdoc />
    public async Task<TransportResponse> InvokeAsync(
        RequestContext context,
        RequestPipelineStep continuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        var method = context.Method.ToString().ToUpperInvariant();
        var uri = Describe(context.RequestUri);

        if (_logger.IsEnabled(_options.RequestLevel))
        {
            _logger.Log(
                _options.RequestLevel,
                "Runax.Http sending {Method} {Uri} over {Transport} (attempt {Attempt}){Correlation}{Headers}",
                method,
                uri,
                context.Transport.Name,
                context.Attempt,
                DescribeCorrelation(context),
                DescribeHeaders(context));
        }

        // Stopwatch rather than DateTime: this is an elapsed-time measurement, and a clock
        // adjustment mid-request would otherwise produce a negative or absurd duration.
        var started = Stopwatch.GetTimestamp();

        TransportResponse response;

        try
        {
            response = await continuation(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller changed their mind. Logged quietly, because it is not a fault and a
            // cancelled shutdown would otherwise fill the log with errors.
            _logger.Log(
                _options.RequestLevel,
                "Runax.Http cancelled {Method} {Uri} after {Elapsed:0.0}ms",
                method,
                uri,
                Elapsed(started));

            throw;
        }
        catch (Exception exception)
        {
            _logger.Log(
                _options.FailureLevel,
                exception,
                "Runax.Http failed {Method} {Uri} over {Transport} after {Elapsed:0.0}ms "
                + "(attempt {Attempt}){Correlation}",
                method,
                uri,
                context.Transport.Name,
                Elapsed(started),
                context.Attempt,
                DescribeCorrelation(context));

            throw;
        }

        var elapsed = Elapsed(started);
        var level = SelectLevel(response.StatusCode, elapsed);

        if (_logger.IsEnabled(level))
        {
            _logger.Log(
                level,
                "Runax.Http received {Status} for {Method} {Uri} over {Transport} after "
                + "{Elapsed:0.0}ms (attempt {Attempt}){Correlation}",
                response.StatusCode,
                method,
                uri,
                context.Transport.Name,
                elapsed,
                context.Attempt,
                DescribeCorrelation(context));
        }

        return response;
    }

    private static double Elapsed(long started)
        => Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private LogLevel SelectLevel(int statusCode, double elapsed)
    {
        // A slow request is worth noticing even when it succeeded, so the threshold can only raise
        // the level, never lower one that is already higher.
        if (_options.SlowRequestThreshold is { } threshold
            && elapsed >= threshold.TotalMilliseconds
            && _options.SlowRequestLevel > SelectStatusLevel(statusCode))
        {
            return _options.SlowRequestLevel;
        }

        return SelectStatusLevel(statusCode);
    }

    private LogLevel SelectStatusLevel(int statusCode)
        => statusCode is >= 200 and <= 299 ? _options.ResponseLevel : _options.ErrorStatusLevel;

    private string Describe(Uri uri)
        => _options.LogQueryString
            ? uri.GetLeftPart(UriPartial.Path) + uri.Query
            : uri.GetLeftPart(UriPartial.Path);

    private static string DescribeCorrelation(RequestContext context)
        => context.CorrelationId is { } id ? $" [{id}]" : string.Empty;

    /// <summary>
    /// The allow-listed headers, with the never-logged ones reduced to their presence.
    /// </summary>
    private string DescribeHeaders(RequestContext context)
    {
        if (_options.LoggedHeaders.Count == 0)
            return string.Empty;

        var described = new List<string>();

        foreach (var (name, value) in context.Headers)
        {
            if (!_options.LoggedHeaders.Contains(name))
                continue;

            var redacted = Array.Exists(
                HttpLoggingOptions.NeverLoggedHeaders,
                never => string.Equals(never, name, StringComparison.OrdinalIgnoreCase));

            described.Add($"{name}: {(redacted ? "***" : value)}");
        }

        return described.Count == 0 ? string.Empty : " " + string.Join(", ", described);
    }
}
