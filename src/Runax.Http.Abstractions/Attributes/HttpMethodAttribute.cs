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

/// <summary>Issues an HTTP GET to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GetAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GetAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public GetAttribute(string template) : base(RequestMethod.Get, template) { }
}

/// <summary>Issues an HTTP POST to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PostAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public PostAttribute(string template) : base(RequestMethod.Post, template) { }
}

/// <summary>Issues an HTTP PUT to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PutAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PutAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public PutAttribute(string template) : base(RequestMethod.Put, template) { }
}

/// <summary>Issues an HTTP PATCH to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PatchAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PatchAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public PatchAttribute(string template) : base(RequestMethod.Patch, template) { }
}

/// <summary>Issues an HTTP DELETE to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DeleteAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="DeleteAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public DeleteAttribute(string template) : base(RequestMethod.Delete, template) { }
}

/// <summary>Issues an HTTP HEAD to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HeadAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="HeadAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public HeadAttribute(string template) : base(RequestMethod.Head, template) { }
}

/// <summary>Issues an HTTP OPTIONS to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OptionsAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="OptionsAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public OptionsAttribute(string template) : base(RequestMethod.Options, template) { }
}
