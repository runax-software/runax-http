using Runax.Http.Serialization;

namespace Runax.Http;

/// <summary>
/// How one client interface is configured: where it points, which transport it goes over, and how
/// its bodies are serialized.
/// </summary>
/// <remarks>
/// One instance per registered contract, so two clients in the same application can target
/// different hosts over different transports without either knowing about the other.
/// </remarks>
public sealed class RunaxClientOptions
{
    /// <summary>
    /// The absolute address request paths are resolved against. Set by
    /// <see cref="BaseAddressAttribute"/> on the interface, and overridden by
    /// <c>WithBaseAddress(...)</c> at registration.
    /// </summary>
    /// <remarks>
    /// A request path appends to this address's path rather than replacing it, so a base of
    /// <c>https://host/api/v2</c> and a path of <c>/users</c> gives <c>https://host/api/v2/users</c>.
    /// </remarks>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The transport this client's requests go over. Overrides <see cref="TransportAttribute"/> on
    /// the interface or method, and is itself overridden by <c>TransportScope.Use(...)</c>.
    /// <see langword="null"/> falls through to the resolver's default.
    /// </summary>
    public string? TransportName { get; set; }

    /// <summary>
    /// How long a request may take when the method does not say. <see langword="null"/> leaves it
    /// to the transport.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// The serializer for request and response bodies. <see langword="null"/> uses the one
    /// registered for the application.
    /// </summary>
    public IContentSerializer? Serializer { get; set; }

    /// <summary>
    /// Headers sent with every request from this client. A header set on the method or a parameter
    /// wins over one here; a null value on the method removes it for that call.
    /// </summary>
    public IDictionary<string, string> DefaultHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to send an <c>Accept</c> header naming the serializer's content type when the
    /// client does not set one itself. On by default — a server that content-negotiates has no
    /// way to know what we can parse otherwise.
    /// </summary>
    public bool SendAcceptHeader { get; set; } = true;

    /// <summary>
    /// How much of an error response body to read into <see cref="HttpApiException.ResponseContent"/>.
    /// </summary>
    /// <remarks>
    /// Error bodies are read eagerly, because the response is disposed before the exception
    /// reaches the caller and the body is usually the only thing that explains the failure. The cap
    /// is there because "unprocessable entity" occasionally comes back as a megabyte of HTML, and
    /// that should not end up in a log line or an exception message.
    /// </remarks>
    public int MaxErrorContentLength { get; set; } = 32 * 1024;
}
