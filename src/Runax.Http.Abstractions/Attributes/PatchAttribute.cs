namespace Runax.Http;

/// <summary>Issues an HTTP PATCH to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PatchAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PatchAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public PatchAttribute(string template) : base(RequestMethod.Patch, template) { }
}
