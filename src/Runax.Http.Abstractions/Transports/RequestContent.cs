namespace Runax.Http.Transports;

/// <summary>
/// The body of a <see cref="TransportRequest"/>, in a form every transport can send.
/// </summary>
/// <remarks>
/// <para>
/// By the time a request reaches a transport the body is already encoded: an object graph has
/// been through <see cref="Runax.Http.Serialization.IContentSerializer"/> and arrives here as
/// <see cref="BinaryContent"/>. Transports do not serialize.
/// </para>
/// <para>
/// Forms and multipart bodies are the exception and stay structured, because boundary
/// generation and field encoding are things each HTTP library does natively and does slightly
/// differently; handing them the parts rather than pre-encoded bytes keeps those differences
/// where they belong.
/// </para>
/// <para>
/// The hierarchy is closed — only this assembly can add a kind — so a transport's
/// <see langword="switch"/> over the content types is exhaustive today and a new kind is a
/// visible break rather than a silent fallthrough.
/// </para>
/// </remarks>
public abstract class RequestContent
{
    private protected RequestContent(string? contentType) => ContentType = contentType;

    /// <summary>
    /// The media type to send, or <see langword="null"/> to let the transport pick the default
    /// for this kind of content.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>Creates content from an in-memory buffer.</summary>
    /// <param name="bytes">The already-encoded body.</param>
    /// <param name="contentType">The media type, for example <c>application/json</c>.</param>
    /// <returns>The content.</returns>
    public static BinaryContent FromBytes(ReadOnlyMemory<byte> bytes, string? contentType = null)
        => new(bytes, contentType);

    /// <summary>Creates content from a string, encoded as UTF-8.</summary>
    /// <param name="value">The body text.</param>
    /// <param name="contentType">The media type, for example <c>text/plain</c>.</param>
    /// <returns>The content.</returns>
    public static BinaryContent FromString(string value, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new BinaryContent(System.Text.Encoding.UTF8.GetBytes(value), contentType);
    }

    /// <summary>
    /// Creates content that is read from a stream as it is sent, rather than buffered first.
    /// </summary>
    /// <param name="stream">The body stream. The transport reads it but does not dispose it.</param>
    /// <param name="contentType">The media type.</param>
    /// <returns>The content.</returns>
    /// <remarks>
    /// Requires <see cref="TransportCapabilities.RequestStreaming"/>. A transport without it
    /// either buffers the stream or rejects the request, per its documented behaviour.
    /// </remarks>
    public static StreamedContent FromStream(Stream stream, string? contentType = null)
        => new(stream, contentType);

    /// <summary>Creates an <c>application/x-www-form-urlencoded</c> body.</summary>
    /// <param name="fields">The form fields. Repeated keys are allowed and preserved in order.</param>
    /// <returns>The content.</returns>
    public static FormContent FromForm(IReadOnlyList<KeyValuePair<string, string?>> fields)
        => new(fields);

    /// <summary>Creates a <c>multipart/form-data</c> body.</summary>
    /// <param name="parts">The parts, in the order they should be written.</param>
    /// <returns>The content.</returns>
    /// <remarks>Requires <see cref="TransportCapabilities.Multipart"/>.</remarks>
    public static MultipartFormContent FromMultipart(IReadOnlyList<MultipartPart> parts)
        => new(parts);
}

/// <summary>An already-encoded body held in memory.</summary>
public sealed class BinaryContent : RequestContent
{
    internal BinaryContent(ReadOnlyMemory<byte> bytes, string? contentType) : base(contentType)
        => Bytes = bytes;

    /// <summary>The encoded body.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
}

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

/// <summary>An <c>application/x-www-form-urlencoded</c> body, as unencoded fields.</summary>
public sealed class FormContent : RequestContent
{
    internal FormContent(IReadOnlyList<KeyValuePair<string, string?>> fields)
        : base("application/x-www-form-urlencoded")
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields;
    }

    /// <summary>The form fields, unencoded. Repeated keys are allowed and order is significant.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Fields { get; }
}

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

/// <summary>One part of a <see cref="MultipartFormContent"/> body.</summary>
public sealed class MultipartPart
{
    /// <summary>Initializes a new instance of the <see cref="MultipartPart"/> class.</summary>
    /// <param name="name">The form field name.</param>
    /// <param name="content">The part's content.</param>
    /// <param name="fileName">The file name to report, for a file part.</param>
    public MultipartPart(string name, RequestContent content, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);

        Name = name;
        Content = content;
        FileName = fileName;
    }

    /// <summary>The form field name.</summary>
    public string Name { get; }

    /// <summary>The part's content. Nested multipart is not supported.</summary>
    public RequestContent Content { get; }

    /// <summary>The file name to report, or <see langword="null"/> for a non-file part.</summary>
    public string? FileName { get; }
}
