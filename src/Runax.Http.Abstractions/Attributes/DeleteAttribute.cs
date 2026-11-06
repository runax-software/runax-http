namespace Runax.Http;

/// <summary>Issues an HTTP DELETE to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DeleteAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="DeleteAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public DeleteAttribute(string template) : base(RequestMethod.Delete, template) { }
}
