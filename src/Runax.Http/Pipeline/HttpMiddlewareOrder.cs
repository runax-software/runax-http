namespace Runax.Http.Pipeline;

/// <summary>
/// Where the built-in stages sit, so your own can be placed relative to them.
/// </summary>
/// <remarks>
/// Lower runs first, which means further from the wire. The gaps are deliberate: there is room to
/// slot something between any two without renumbering.
/// </remarks>
public static class HttpMiddlewareOrder
{
    /// <summary>
    /// Correlation, outermost, so every stage inside it — and every log line — sees the same id.
    /// </summary>
    public const int Correlation = 100;

    /// <summary>
    /// Logging, inside correlation so it can report the id, and outside authentication so the
    /// elapsed time it measures includes acquiring a token. That wait is latency the caller
    /// experiences, so leaving it out would make the number a quiet lie.
    /// </summary>
    public const int Logging = 200;

    /// <summary>
    /// Authentication, innermost, so the token is attached as late as possible. On a retry that
    /// means a token refreshed in the meantime is the one that gets used.
    /// </summary>
    public const int Authentication = 300;
}
