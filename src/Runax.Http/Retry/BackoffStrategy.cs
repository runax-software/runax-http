namespace Runax.Http.Retry;

/// <summary>How the wait between attempts grows.</summary>
public enum BackoffStrategy
{
    /// <summary>The same wait every time.</summary>
    Constant,

    /// <summary>The wait grows by the base delay each attempt: 1s, 2s, 3s.</summary>
    Linear,

    /// <summary>
    /// The wait doubles each attempt: 1s, 2s, 4s. The default, because a service that is
    /// struggling is made worse by a client that retries at a fixed rate.
    /// </summary>
    Exponential
}
