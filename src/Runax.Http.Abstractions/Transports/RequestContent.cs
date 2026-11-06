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
