using Runax.Http.Transports;

namespace Runax.Http.Requests;

/// <summary>
/// Builds a body from a value that is already in wire form, for <c>[Body(Format = BodyFormat.Raw)]</c>.
/// </summary>
/// <remarks>
/// Called from generated code. The overloads are the whole supported set: anything else is a
/// compile error at the generated call site rather than a reflective guess at how to encode it.
/// </remarks>
public static class RawBody
{
    /// <summary>Sends a string as UTF-8.</summary>
    /// <param name="value">The body text.</param>
    /// <param name="contentType">The media type.</param>
    /// <returns>The content.</returns>
    public static RequestContent Create(string value, string? contentType = "text/plain")
        => RequestContent.FromString(value, contentType);

    /// <summary>Sends bytes as-is.</summary>
    /// <param name="value">The body.</param>
    /// <param name="contentType">The media type.</param>
    /// <returns>The content.</returns>
    public static RequestContent Create(byte[] value, string? contentType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(value);

        return RequestContent.FromBytes(value, contentType);
    }

    /// <inheritdoc cref="Create(byte[], string?)" />
    public static RequestContent Create(
        ReadOnlyMemory<byte> value,
        string? contentType = "application/octet-stream")
        => RequestContent.FromBytes(value, contentType);

    /// <summary>
    /// Streams the body rather than buffering it, which needs a transport that supports request
    /// streaming.
    /// </summary>
    /// <param name="value">The body stream. Not disposed here.</param>
    /// <param name="contentType">The media type.</param>
    /// <returns>The content.</returns>
    public static RequestContent Create(Stream value, string? contentType = "application/octet-stream")
        => RequestContent.FromStream(value, contentType);
}
