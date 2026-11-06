namespace Runax.Http.Transports;

/// <summary>A body streamed from a <see cref="Stream"/> as the request is sent.</summary>
public sealed class StreamedContent : RequestContent
{
    internal StreamedContent(Stream stream, string? contentType) : base(contentType)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Stream = stream;
    }

    /// <summary>
    /// The body stream. Owned by the caller: a transport reads from it and must not dispose it.
    /// </summary>
    public Stream Stream { get; }
}
