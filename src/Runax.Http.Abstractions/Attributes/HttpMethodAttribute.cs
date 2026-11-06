namespace Runax.Http;

/// <summary>
/// Base type for the attributes that mark a client interface method as an HTTP request.
/// </summary>
/// <remarks>
/// The template is a path relative to the client's base address and may contain
/// <c>{name}</c> placeholders, each of which must be satisfied by a
/// <see cref="PathAttribute"/>-annotated parameter (or, failing that, by a parameter of the
/// same name). Placeholder resolution happens at compile time in the generator, so a
/// mismatch is a build error rather than a runtime failure.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public abstract class HttpMethodAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpMethodAttribute"/> class.
    /// </summary>
    /// <param name="method">The HTTP method to issue.</param>
    /// <param name="template">The request path relative to the base address.</param>
    protected HttpMethodAttribute(RequestMethod method, string template)
    {
        Method = method;
        Template = template;
    }

    /// <summary>The HTTP method to issue.</summary>
    public RequestMethod Method { get; }

    /// <summary>The request path relative to the base address, with optional <c>{name}</c> placeholders.</summary>
    public string Template { get; }
}
