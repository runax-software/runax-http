namespace Runax.Http;

/// <summary>
/// Binds a parameter to the request body. At most one parameter per method may carry this.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute
{
    /// <summary>How the value is encoded onto the wire. Defaults to <see cref="BodyFormat.Json"/>.</summary>
    public BodyFormat Format { get; set; } = BodyFormat.Json;
}
