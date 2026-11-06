namespace Runax.Http.Transports;

/// <summary>An <c>application/x-www-form-urlencoded</c> body, as unencoded fields.</summary>
public sealed class FormContent : RequestContent
{
    internal FormContent(IReadOnlyList<KeyValuePair<string, string?>> fields)
        : base("application/x-www-form-urlencoded")
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields;
    }

    /// <summary>The form fields, unencoded. Repeated keys are allowed and order is significant.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Fields { get; }
}
