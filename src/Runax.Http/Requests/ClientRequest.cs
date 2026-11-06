using Runax.Http.Transports;

namespace Runax.Http.Requests;

/// <summary>
/// What a client method asked for, before any of it has been resolved: the path template rather
/// than a URL, the arguments rather than a query string.
/// </summary>
/// <remarks>
/// <para>
/// This is the hand-off from generated code to the runtime. Generated methods build one of these
/// and pass it to <see cref="IHttpContractExecutor"/>; everything that follows — template
/// substitution, query encoding, header merging, base address resolution, transport selection,
/// response mapping — happens once in the runtime rather than being emitted into every method.
/// </para>
/// <para>
/// Keeping the generated code this thin is deliberate. Logic in a generator is logic you debug
/// by reading generated output, and fix by shipping a new compiler package to everyone.
/// </para>
/// </remarks>
public sealed class ClientRequest
{
    /// <summary>The HTTP method the client method is annotated with.</summary>
    public required RequestMethod Method { get; init; }

    /// <summary>
    /// The path template, with <c>{placeholder}</c> segments still in place and relative to the
    /// client's base address.
    /// </summary>
    public required string Template { get; init; }

    /// <summary>
    /// Values for the template's placeholders, keyed by placeholder name. Values are formatted
    /// invariantly and escaped as a single path segment.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> PathValues { get; init; } = [];

    /// <summary>
    /// Query arguments, keyed by query name. A collection value expands to one repeated key per
    /// item; a null value is omitted rather than sent empty.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> QueryValues { get; init; } = [];

    /// <summary>
    /// Headers from the interface, the method and its parameters. Merged over the client's
    /// configured defaults; a null value removes an inherited default rather than sending it empty.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Headers { get; init; } = [];

    /// <summary>
    /// The body, already serialized. Generated code calls the configured
    /// <see cref="Runax.Http.Serialization.IContentSerializer"/> with the parameter's static type,
    /// so no reflection over the body is needed at any point.
    /// </summary>
    public RequestContent? Content { get; init; }

    /// <summary>
    /// The transport named by <see cref="TransportAttribute"/> on the method or interface, if any.
    /// The weakest of the three ways to choose a transport — see
    /// <see cref="IHttpContractExecutor"/> for the precedence.
    /// </summary>
    public string? TransportName { get; init; }

    /// <summary>A per-request timeout, overriding the client's configured one.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>A per-request proxy. Requires a transport with per-request proxy support.</summary>
    public RequestProxy? Proxy { get; init; }
}
