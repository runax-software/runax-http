namespace Runax.Http;

/// <summary>Issues an HTTP OPTIONS to the given path.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OptionsAttribute : HttpMethodAttribute
{
    /// <summary>Initializes a new instance of the <see cref="OptionsAttribute"/> class.</summary>
    /// <param name="template">The request path relative to the base address.</param>
    public OptionsAttribute(string template) : base(RequestMethod.Options, template) { }
}
