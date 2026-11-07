using Microsoft.Extensions.Logging;

namespace Runax.Http.Tests.Fakes;

/// <summary>One line the logging stage wrote.</summary>
internal sealed record LoggedLine(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// An <see cref="ILogger"/> that keeps what it was told, so a test can assert on it.
/// </summary>
/// <remarks>
/// Hand-written rather than pulled from a testing package: what these tests need is the rendered
/// message — the thing that would reach a log aggregator and leak a credential — and a few lines of
/// capture gets that without another dependency.
/// </remarks>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<LoggedLine> _lines = [];
    private readonly LogLevel _minimum;

    internal CapturingLogger(LogLevel minimum = LogLevel.Trace) => _minimum = minimum;

    internal IReadOnlyList<LoggedLine> Lines => _lines;

    /// <summary>Everything written, joined, for asserting that a secret appears nowhere at all.</summary>
    internal string AllText => string.Join("\n", _lines.Select(line => line.Message));

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        _lines.Add(new LoggedLine(logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
            // Nothing to unwind; scopes are not what these tests are about.
        }
    }
}
