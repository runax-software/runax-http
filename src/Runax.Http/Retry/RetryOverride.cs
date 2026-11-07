namespace Runax.Http.Retry;

/// <summary>
/// What a method's <see cref="RetryAttribute"/> asked for, as the runtime receives it.
/// </summary>
/// <remarks>
/// <para>
/// Generated code constructs one of these from the attribute it found at compile time, so nothing
/// reads the attribute at run time. It carries only what the attribute can express; everything
/// else comes from the client's <see cref="RetryOptions"/>.
/// </para>
/// <para>
/// A separate type from <see cref="RetryOptions"/> on purpose. Options are a mutable bag with a
/// <see cref="TimeProvider"/> and a status-code set, built once at registration; this is a small
/// immutable description of a declaration in source, and conflating the two would mean generated
/// code constructing and partially populating a configuration object.
/// </para>
/// </remarks>
public sealed class RetryOverride
{
    /// <summary>Initializes a new instance of the <see cref="RetryOverride"/> class.</summary>
    /// <param name="maxAttempts">Total attempts including the first.</param>
    /// <param name="delayMilliseconds">
    /// The base delay in milliseconds, or <see cref="RetryAttribute.Inherit"/> to keep the
    /// client's.
    /// </param>
    /// <param name="allowNonIdempotent">
    /// Whether this method may be retried despite not being idempotent.
    /// </param>
    public RetryOverride(
        int maxAttempts,
        int delayMilliseconds = RetryAttribute.Inherit,
        bool allowNonIdempotent = false)
    {
        MaxAttempts = maxAttempts;
        DelayMilliseconds = delayMilliseconds;
        AllowNonIdempotent = allowNonIdempotent;
    }

    /// <summary>Total attempts including the first. <c>1</c> means this method is not retried.</summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// The base delay in milliseconds, or <see cref="RetryAttribute.Inherit"/> to keep the
    /// client's.
    /// </summary>
    public int DelayMilliseconds { get; }

    /// <summary>Whether this method may be retried despite not being idempotent.</summary>
    public bool AllowNonIdempotent { get; }
}
