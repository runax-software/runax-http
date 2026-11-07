using System.Diagnostics;
using System.Diagnostics.Metrics;
using Runax.Http.Pipeline;

namespace Runax.Http.Tests.Fakes;

/// <summary>
/// Subscribes to the library's <see cref="ActivitySource"/> and <see cref="Meter"/> and keeps what
/// they emit.
/// </summary>
/// <remarks>
/// <para>
/// The real listener APIs rather than a seam of our own. The whole claim being tested is "an
/// OpenTelemetry setup will see this", and OpenTelemetry sees it through
/// <see cref="ActivityListener"/> and <see cref="MeterListener"/> — a fake the middleware reported
/// to instead would prove the middleware talks to the fake.
/// </para>
/// <para>
/// Filtered to this source by name, so a test is not handed activities started by something else
/// in the process.
/// </para>
/// </remarks>
internal sealed class TelemetryRecorder : IDisposable
{
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;
    private readonly List<Activity> _stopped = [];
    private readonly List<Measurement> _measurements = [];
    private readonly object _gate = new();

    internal TelemetryRecorder()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RunaxHttpTelemetry.ActivitySourceName,

            // AllDataAndRecorded, not PropagationData: a sampler that declines leaves StartActivity
            // returning an activity with no tags, and every attribute assertion would pass
            // vacuously against it.
            Sample = (ref ActivityCreationOptions<ActivityContext> _)
                => ActivitySamplingResult.AllDataAndRecorded,

            ActivityStopped = activity =>
            {
                lock (_gate)
                    _stopped.Add(activity);
            }
        };

        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RunaxHttpTelemetry.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };

        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            var copied = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
                copied[tag.Key] = tag.Value;

            lock (_gate)
                _measurements.Add(new Measurement(instrument.Name, instrument.Unit, value, copied));
        });

        _meters.Start();
    }

    /// <summary>Spans that have finished, oldest first.</summary>
    internal IReadOnlyList<Activity> Activities
    {
        get { lock (_gate) return [.. _stopped]; }
    }

    /// <summary>The single span recorded, failing the test if there is not exactly one.</summary>
    internal Activity Activity
    {
        get
        {
            var all = Activities;
            all.Count.ShouldBe(1);
            return all[0];
        }
    }

    internal IReadOnlyList<Measurement> Measurements
    {
        get { lock (_gate) return [.. _measurements]; }
    }

    internal Measurement Duration
    {
        get
        {
            var all = Measurements
                .Where(m => m.Instrument == RunaxHttpTelemetry.RequestDurationInstrumentName)
                .ToList();

            all.Count.ShouldBe(1);
            return all[0];
        }
    }

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }

    internal sealed record Measurement(
        string Instrument,
        string? Unit,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
