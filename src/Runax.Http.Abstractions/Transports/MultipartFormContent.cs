namespace Runax.Http.Transports;

/// <summary>A <c>multipart/form-data</c> body, as parts. The transport generates the boundary.</summary>
public sealed class MultipartFormContent : RequestContent
{
    internal MultipartFormContent(IReadOnlyList<MultipartPart> parts) : base("multipart/form-data")
    {
        ArgumentNullException.ThrowIfNull(parts);
        Parts = parts;
    }

    /// <summary>The parts, in the order they should be written.</summary>
    public IReadOnlyList<MultipartPart> Parts { get; }
}
