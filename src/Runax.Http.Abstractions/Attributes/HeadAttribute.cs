namespace Runax.Http;

/// <summary>Issues an HTTP HEAD to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HeadAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="HeadAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public HeadAttribute(string template) : base(RequestMethod.Head, template) { }
}
