using Microsoft.CodeAnalysis;

namespace Runax.Http.SourceGenerator;

/// <summary>
/// Every mistake the generator can see at compile time.
/// </summary>
/// <remarks>
/// This list is the main argument for a generator over a runtime proxy. A reflection-based client
/// discovers a mismatched placeholder or an unbindable parameter on the first call that hits it,
/// in whatever environment gets there first. Here they are build errors, with a location and a
/// sentence explaining the fix.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Runax.Http";

    internal static readonly DiagnosticDescriptor ContractMustBeInterface = Error(
        "RXH001",
        "Request attributes are only valid on interfaces",
        "'{0}' is not an interface, so no client can be generated for it. Runax.Http implements "
        + "interfaces; move these methods to an interface and inject that.");

    internal static readonly DiagnosticDescriptor UnsupportedReturnType = Error(
        "RXH002",
        "Unsupported client method return type",
        "'{0}' returns '{1}', which a client method cannot. Use Task, Task<T>, Task<ApiResponse> or "
        + "Task<ApiResponse<T>>. HTTP is asynchronous, so a synchronous signature could only be "
        + "implemented by blocking, which deadlocks often enough that it is not offered.");

    internal static readonly DiagnosticDescriptor UnmatchedPathPlaceholder = Error(
        "RXH003",
        "Path template placeholder has no parameter",
        "The template '{0}' contains '{{{1}}}', but '{2}' has no parameter bound to it. Add a "
        + "parameter named '{1}', or mark one with [Path(\"{1}\")].");

    internal static readonly DiagnosticDescriptor MultipleBodyParameters = Error(
        "RXH004",
        "A request can have only one body",
        "'{0}' marks more than one parameter with [Body]. A request has a single body; combine the "
        + "values into one type, or send the rest as query parameters or headers.");

    internal static readonly DiagnosticDescriptor UnboundParameter = Error(
        "RXH005",
        "Parameter is not bound to anything",
        "'{0}' on '{1}' is not bound to any part of the request. Mark it with [Query], [Path], "
        + "[Body] or [Header], or name it after a placeholder in the path template. Sending it "
        + "nowhere silently is the one thing that would be worse than this error.");

    internal static readonly DiagnosticDescriptor MultipleCancellationTokens = Error(
        "RXH006",
        "A method can take only one CancellationToken",
        "'{0}' takes more than one CancellationToken. Keep one, as the last parameter.");

    internal static readonly DiagnosticDescriptor GenericContract = Error(
        "RXH007",
        "Client interfaces cannot be generic",
        "'{0}' is generic. A client is generated once per interface, and a generic interface has no "
        + "single implementation to generate. Declare a non-generic interface per concrete API.");

    internal static readonly DiagnosticDescriptor GenericMethod = Error(
        "RXH008",
        "Client methods cannot be generic",
        "'{0}' is generic. The body and response types have to be known at compile time for "
        + "serialization to work without reflection, so a type parameter cannot stand in for them.");

    internal static readonly DiagnosticDescriptor MissingRequestAttribute = Error(
        "RXH009",
        "Client interface member has no request attribute",
        "'{0}' has no request attribute, so the generated client could not implement it and would "
        + "not compile. Mark it with [Get], [Post], [Put], [Patch], [Delete], [Head] or [Options].");

    internal static readonly DiagnosticDescriptor BodyOnBodylessMethod = Warning(
        "RXH010",
        "This method's HTTP method does not normally carry a body",
        "'{0}' sends a body with {1}. It will be sent, but caches, proxies and servers are entitled "
        + "to ignore or reject it, so this is usually a [Query] parameter that was marked [Body] by "
        + "mistake.");

    internal static readonly DiagnosticDescriptor InvalidBaseAddress = Error(
        "RXH011",
        "[BaseAddress] must be an absolute URI",
        "[BaseAddress(\"{0}\")] on '{1}' is not an absolute URI. Include the scheme and host, as in "
        + "\"https://api.example.com\".");

    internal static readonly DiagnosticDescriptor DuplicatePathBinding = Error(
        "RXH012",
        "Two parameters are bound to the same placeholder",
        "'{0}' binds more than one parameter to '{{{1}}}'. Only one value can fill a placeholder, so "
        + "which one wins would be arbitrary.");

    internal static readonly DiagnosticDescriptor UnsupportedMember = Error(
        "RXH013",
        "Client interfaces can only declare methods",
        "'{0}' declares a {1}, which a generated client cannot implement. A client interface "
        + "describes requests, and a request is a method call.");

    internal static readonly DiagnosticDescriptor UnsupportedBodyFormat = Error(
        "RXH014",
        "This body format is not implemented yet",
        "'{0}' asks for BodyFormat.{1}, which the generator does not emit yet. Use BodyFormat.Json, "
        + "or BodyFormat.Raw with a string, byte[] or Stream, and build the form or multipart body "
        + "yourself for now. Tracked on the roadmap rather than silently sent as JSON.");

    private static DiagnosticDescriptor Error(string id, string title, string format)
        => new(id, title, format, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static DiagnosticDescriptor Warning(string id, string title, string format)
        => new(id, title, format, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
