using Runax.Http.Transports;

namespace Runax.Http.Requests;

/// <summary>
/// Builds an <c>application/x-www-form-urlencoded</c> body, for
/// <c>[Body(Format = BodyFormat.UrlEncoded)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Called from generated code, with the body parameter passed straight through.
/// </para>
/// <para>
/// Notably absent is an arbitrary object whose properties become fields. That would need reflection
/// over the type at run time, which is the one thing this library does not do — so a POCO is a
/// compile-time error (<c>RXH019</c>) rather than a trimming surprise. Use
/// <see cref="BodyFormat.Json"/>, or project the object into a dictionary at the call site where
/// the property names are visible.
/// </para>
/// </remarks>
public static class FormBody
{
    /// <summary>Builds a form body from a sequence of fields.</summary>
    /// <typeparam name="TValue">
    /// The field value type. Anything: <c>string</c>, a formattable value, or a collection.
    /// </typeparam>
    /// <param name="fields">The fields. Repeated keys are allowed and order is preserved.</param>
    /// <returns>The content.</returns>
    /// <remarks>
    /// <para>
    /// Generic in the value type rather than taking
    /// <c>IEnumerable&lt;KeyValuePair&lt;string, string?&gt;&gt;</c>, which looks simpler and is
    /// not: a <c>Dictionary&lt;string, string&gt;</c> passed to that signature is a CS8620
    /// nullability warning, so generated code would fail to compile in any project with
    /// warnings-as-errors. One generic overload accepts every spelling without the friction, and
    /// nullable annotations stop mattering.
    /// </para>
    /// <para>
    /// Values are formatted by the same formatter as path, query and header values, so a
    /// <see cref="DateTimeOffset"/> spells the same thing in a form field as in a query string. A
    /// collection value becomes repeated fields, the way a collection query argument becomes
    /// repeated keys.
    /// </para>
    /// </remarks>
    public static RequestContent Create<TValue>(IEnumerable<KeyValuePair<string, TValue>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var materialized = new List<KeyValuePair<string, string?>>();

        foreach (var (key, value) in fields)
        {
            switch (value)
            {
                // A null field is sent empty rather than dropped. Unlike a query parameter, where
                // null means "omit", a form field that was named has to be present: dropping it
                // changes which fields the server sees as supplied at all.
                case null:
                    materialized.Add(new KeyValuePair<string, string?>(key, string.Empty));
                    break;

                // Short-circuited so a string is never treated as a collection of characters.
                case string text:
                    materialized.Add(new KeyValuePair<string, string?>(key, text));
                    break;

                default:
                    foreach (var formatted in RequestValueFormatter.Expand(value))
                        materialized.Add(new KeyValuePair<string, string?>(key, formatted));

                    break;
            }
        }

        return RequestContent.FromForm(materialized);
    }
}
