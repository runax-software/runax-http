namespace Runax.Http;

/// <summary>Issues an HTTP GET to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GetAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="GetAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public GetAttribute(string template) : base(RequestMethod.Get, template) { }
}
