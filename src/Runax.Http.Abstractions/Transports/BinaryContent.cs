namespace Runax.Http.Transports;

/// <summary>An already-encoded body held in memory.</summary>
public sealed class BinaryContent : RequestContent
{
    internal BinaryContent(ReadOnlyMemory<byte> bytes, string? contentType) : base(contentType)
        => Bytes = bytes;

    /// <summary>The encoded body.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
}
