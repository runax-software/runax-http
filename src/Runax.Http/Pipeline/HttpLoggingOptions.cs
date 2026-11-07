using Microsoft.Extensions.Logging;

namespace Runax.Http.Pipeline;

/// <summary>What the logging stage writes, and at what level.</summary>
/// <remarks>
/// <para>
/// There is no option to log request or response bodies, and that is deliberate rather than
/// unfinished. A body is the most likely place for a password, a token or someone's personal data,
/// application logs are routinely shipped to a third-party aggregator and kept for months, and a
/// switch like that gets turned on during an incident and left on. If you need to see a body, a
/// debugger or a local proxy shows it to you without writing it down somewhere permanent.
/// </para>
/// <para>
/// Header values are off by default for the same reason — see <see cref="LoggedHeaders"/>.
/// </para>
/// </remarks>
public sealed class HttpLoggingOptions
{
    /// <summary>
    /// Headers whose values are never written, whatever <see cref="LoggedHeaders"/> says.
    /// </summary>
    /// <remarks>
    /// Naming one of these in <see cref="LoggedHeaders"/> logs <c>***</c> instead of the value, so
    /// a log line can still answer "was authentication even attached?" without carrying the
    /// credential. There is no way to turn this off: a bearer token in a log aggregator is a
    /// credential leak whoever asked for it, and an option that can produce one would eventually
    /// be used.
    /// </remarks>
    public static readonly string[] NeverLoggedHeaders =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie"
    ];

    /// <summary>The level a request is announced at before it is sent.</summary>
    public LogLevel RequestLevel { get; set; } = LogLevel.Debug;

    /// <summary>The level a successful response is reported at.</summary>
    public LogLevel ResponseLevel { get; set; } = LogLevel.Information;

    /// <summary>The level a non-success status is reported at.</summary>
    /// <remarks>
    /// Higher than a success by default. A 404 that a method handles is routine, but it is also the
    /// first thing anyone looks for, and a response that something treats as a fault should not be
    /// at the same level as one that worked.
    /// </remarks>
    public LogLevel ErrorStatusLevel { get; set; } = LogLevel.Warning;

    /// <summary>The level a request that never completed is reported at.</summary>
    public LogLevel FailureLevel { get; set; } = LogLevel.Error;

    /// <summary>
    /// Whether the query string is included in the logged address.
    /// </summary>
    /// <remarks>
    /// On, because a path without its query rarely identifies what was asked for. Turn it off if
    /// your API takes a credential as a query parameter — though
    /// <see href="https://github.com/runax-software/runax-http/blob/main/docs/diagnostics.md">RXH005</see>
    /// exists partly to stop that happening by accident in the first place.
    /// </remarks>
    public bool LogQueryString { get; set; } = true;

    /// <summary>
    /// The headers whose values may be logged. Empty by default, so none are.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than a deny-list, which is the whole point: a deny-list protects the
    /// headers someone thought of, and the one that leaks is always the one nobody thought of.
    /// Names in <see cref="NeverLoggedHeaders"/> are logged as <c>***</c> even when listed here.
    /// </remarks>
    public ISet<string> LoggedHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long a request may take before it is reported at <see cref="SlowRequestLevel"/>
    /// regardless of outcome. <see langword="null"/> disables it.
    /// </summary>
    public TimeSpan? SlowRequestThreshold { get; set; }

    /// <summary>The level a request slower than <see cref="SlowRequestThreshold"/> is reported at.</summary>
    public LogLevel SlowRequestLevel { get; set; } = LogLevel.Warning;
}
