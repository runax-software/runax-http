namespace Runax.Http;

/// <summary>Issues an HTTP POST to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PostAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public PostAttribute(string template) : base(RequestMethod.Post, template) { }
}
