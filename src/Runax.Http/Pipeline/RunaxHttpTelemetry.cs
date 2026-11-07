using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Runax.Http.Pipeline;

/// <summary>
/// The names and instruments Runax.Http emits telemetry under.
/// </summary>
/// <remarks>
/// <para>
/// Public because an OpenTelemetry setup has to name them:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(RunaxHttpTelemetry.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(RunaxHttpTelemetry.MeterName));
/// </code>
/// <para>
/// Attribute names follow the OpenTelemetry HTTP client semantic conventions, so an existing
/// dashboard or alert built for <c>HttpClient</c> keeps working when a call starts going through a
/// Runax.Http client — the point of a convention being that you do not have to re-learn it per
/// library. The transport is the one addition, under a <c>runax.</c> prefix because inventing an
/// unprefixed attribute in someone else's namespace is how conventions rot.
/// </para>
/// </remarks>
public static class RunaxHttpTelemetry
{
    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name to add to a tracer.</summary>
    public const string ActivitySourceName = "Runax.Http";

    /// <summary>The <see cref="System.Diagnostics.Metrics.Meter"/> name to add to a meter provider.</summary>
    public const string MeterName = "Runax.Http";

    /// <summary>
    /// The request duration histogram, named and in the unit the HTTP semantic conventions
    /// prescribe.
    /// </summary>
    /// <remarks>
    /// Seconds, not milliseconds. The convention says seconds, and a histogram whose unit differs
    /// from every other HTTP duration in the same backend cannot be compared with them — which is
    /// most of what you want a duration for.
    /// </remarks>
    public const string RequestDurationInstrumentName = "http.client.request.duration";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, ThisVersion);

    internal static readonly Meter Meter = new(MeterName, ThisVersion);

    internal static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        RequestDurationInstrumentName,
        unit: "s",
        description: "Duration of an outbound HTTP request attempt.");

    private static string ThisVersion =>
        typeof(RunaxHttpTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
