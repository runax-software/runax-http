using Runax.Http.Transports;

namespace Runax.Http.Requests;

/// <summary>
/// Builds a <c>multipart/form-data</c> body, for <c>[Body(Format = BodyFormat.Multipart)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Called from generated code. The parameter is a sequence of <see cref="MultipartPart"/>, which
/// you build at the call site — there is no mapping from an object's properties to parts, because
/// that would mean reflecting over the type at run time.
/// </para>
/// <para>
/// The parts stay structured all the way to the transport: boundary generation and field encoding
/// are genuinely native to each HTTP library, so handing them the parts beats handing them bytes.
/// Requires a transport declaring <see cref="TransportCapabilities.Multipart"/>, which is checked
/// before the request is sent.
/// </para>
/// </remarks>
public static class MultipartBody
{
    /// <summary>Builds a multipart body from its parts.</summary>
    /// <param name="parts">The parts, in the order they should be written.</param>
    /// <returns>The content.</returns>
    public static RequestContent Create(IEnumerable<MultipartPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return RequestContent.FromMultipart(parts.ToList());
    }
}
