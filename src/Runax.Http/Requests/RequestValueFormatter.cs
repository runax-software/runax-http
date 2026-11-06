using System.Collections;
using System.Globalization;

namespace Runax.Http.Requests;

/// <summary>
/// Turns a path or query argument into the text that goes on the wire.
/// </summary>
/// <remarks>
/// <para>
/// One implementation, applied to every client and every transport. Letting each transport
/// format values would mean <c>?since=2026-01-01T00:00:00Z</c> on one and
/// <c>?since=01/01/2026 00:00:00</c> on another, from the same interface and the same argument —
/// and the second one depends on the server's locale to even be parseable.
/// </para>
/// <para>
/// Everything is invariant-culture. There is no case where a caller wants their machine's locale
/// to decide what a remote API receives.
/// </para>
/// </remarks>
internal static class RequestValueFormatter
{
    /// <summary>
    /// Formats a single value, or returns <see langword="null"/> for a value that should be
    /// omitted entirely rather than sent empty.
    /// </summary>
    internal static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,

        // "True"/"False" is .NET's spelling, not the web's. Every JSON and OpenAPI-described API
        // expects lowercase, and the ones that accept "True" do so by accident.
        bool flag => flag ? "true" : "false",

        // Round-trippable and unambiguous. The default ToString() for these is locale-shaped and
        // loses the offset, which turns a timestamp into a guess about the server's time zone.
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),

        Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
        Uri uri => uri.ToString(),

        // Enums before IFormattable: Enum implements it, but passing a format of null yields the
        // member name, which is what an API spelling a parameter as a string expects.
        Enum enumeration => enumeration.ToString(),

        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    /// <summary>
    /// Expands a value into the one or more strings it contributes. A collection becomes one entry
    /// per item, which is how a repeated query key is expressed; everything else is a single entry.
    /// </summary>
    /// <remarks>
    /// Null items inside a collection are skipped rather than sent empty, matching how a null
    /// argument is skipped. Strings are deliberately not treated as collections of characters.
    /// </remarks>
    internal static List<string> Expand(object? value)
    {
        switch (value)
        {
            case null:
                return [];

            case string text:
                return [text];

            case IEnumerable enumerable:
                {
                    var expanded = new List<string>();

                    foreach (var item in enumerable)
                    {
                        var formatted = Format(item);
                        if (formatted is not null)
                            expanded.Add(formatted);
                    }

                    return expanded;
                }

            default:
                {
                    var formatted = Format(value);
                    return formatted is null ? [] : [formatted];
                }
        }
    }
}
