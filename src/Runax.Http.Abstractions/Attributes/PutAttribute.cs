namespace Runax.Http;

/// <summary>Issues an HTTP PUT to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PutAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="PutAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public PutAttribute(string template) : base(RequestMethod.Put, template) { }
}
