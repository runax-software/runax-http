using System.Text;

namespace Runax.Http.SourceGenerator;

/// <summary>
/// Turns a C# identifier into a query key, following the declared convention.
/// </summary>
/// <remarks>
/// <para>
/// Runs in the generator, so the result is baked into the emitted code as a literal. The runtime
/// never transforms a name — which is why a policy costs nothing per request, and why the key you
/// see in generated output is exactly the key that reaches the wire.
/// </para>
/// <para>
/// Word splitting matches what a reader expects of these conventions rather than a naive
/// "uppercase means new word": an acronym run is one word, so <c>HTTPStatus</c> is
/// <c>http_status</c> and not <c>h_t_t_p_status</c>, and a digit run is its own word, so
/// <c>Top10Items</c> is <c>top_10_items</c>.
/// </para>
/// </remarks>
internal static class QueryNameFormatter
{
    /// <summary>The <see cref="QueryNaming"/> member names, as the generator sees them.</summary>
    internal const string CamelCase = "CamelCase";
    internal const string AsDeclared = "AsDeclared";
    internal const string SnakeCaseLower = "SnakeCaseLower";
    internal const string SnakeCaseUpper = "SnakeCaseUpper";
    internal const string KebabCaseLower = "KebabCaseLower";
    internal const string KebabCaseUpper = "KebabCaseUpper";

    /// <summary>Applies a naming convention to an identifier.</summary>
    /// <param name="identifier">The parameter or property name as written.</param>
    /// <param name="naming">The convention, as a <see cref="QueryNaming"/> member name.</param>
    /// <returns>The query key.</returns>
    internal static string Apply(string identifier, string naming)
    {
        if (string.IsNullOrEmpty(identifier) || naming == AsDeclared)
            return identifier;

        return naming switch
        {
            SnakeCaseLower => Join(identifier, '_', upper: false),
            SnakeCaseUpper => Join(identifier, '_', upper: true),
            KebabCaseLower => Join(identifier, '-', upper: false),
            KebabCaseUpper => Join(identifier, '-', upper: true),
            _ => ToCamelCase(identifier)
        };
    }

    /// <summary>
    /// Lowercases the leading uppercase run, leaving the last of it capitalised when a lowercase
    /// letter follows — so <c>ID</c> is <c>id</c> and <c>HTTPStatus</c> is <c>httpStatus</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately the same rule as <c>JsonNamingPolicy.CamelCase</c>, so a type sent as a query
    /// object and as a JSON body spells its fields identically. That symmetry is the reason
    /// camelCase is the default at all.
    /// </remarks>
    private static string ToCamelCase(string identifier)
    {
        if (!char.IsUpper(identifier[0]))
            return identifier;

        var buffer = identifier.ToCharArray();

        for (var i = 0; i < buffer.Length; i++)
        {
            if (i > 0 && i + 1 < buffer.Length && !char.IsUpper(buffer[i + 1]))
                break;

            buffer[i] = char.ToLowerInvariant(buffer[i]);
        }

        return new string(buffer);
    }

    private static string Join(string identifier, char separator, bool upper)
    {
        var result = new StringBuilder(identifier.Length + 4);

        foreach (var word in SplitWords(identifier))
        {
            if (result.Length > 0)
                result.Append(separator);

            result.Append(upper ? word.ToUpperInvariant() : word.ToLowerInvariant());
        }

        return result.Length == 0 ? identifier : result.ToString();
    }

    /// <summary>
    /// Splits an identifier into words at the boundaries a reader would see.
    /// </summary>
    /// <remarks>
    /// Three boundaries: lower-to-upper (<c>perPage</c>), the end of an acronym run followed by a
    /// lowercase letter (<c>HTTPStatus</c> breaks before <c>Status</c>, not after <c>H</c>), and
    /// either side of a digit run. Underscores and separators already present are treated as
    /// boundaries and dropped, so an identifier that is already snake_case survives the trip.
    /// </remarks>
    private static List<string> SplitWords(string identifier)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < identifier.Length; i++)
        {
            var character = identifier[i];

            if (character is '_' or '-' or ' ')
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                var previous = identifier[i - 1];

                var startsWord =
                    (char.IsUpper(character) && !char.IsUpper(previous))
                    || (char.IsUpper(character) && char.IsUpper(previous)
                                                && i + 1 < identifier.Length
                                                && char.IsLower(identifier[i + 1]))
                    || (char.IsDigit(character) != char.IsDigit(previous));

                if (startsWord)
                    Flush();
            }

            current.Append(character);
        }

        Flush();

        return words;

        void Flush()
        {
            if (current.Length == 0)
                return;

            words.Add(current.ToString());
            current.Clear();
        }
    }
}
