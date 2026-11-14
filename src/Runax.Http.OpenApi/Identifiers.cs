using System.Text;
using Runax.Http.SourceGenerator;

namespace Runax.Http.OpenApi;

/// <summary>
/// Turns names from an OpenAPI document — operation ids, schema names, wire names with dashes and
/// underscores in them — into C# identifiers.
/// </summary>
internal static class Identifiers
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
        "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// <c>pet_id</c>, <c>pet-id</c> and <c>petId</c> all become <c>PetId</c>. The rest of each word
    /// is kept as written, so an acronym the document spells <c>ID</c> stays <c>ID</c>.
    /// </summary>
    internal static string Pascal(string raw)
    {
        var result = new StringBuilder(raw.Length);
        var startOfWord = true;

        foreach (var character in raw)
        {
            if (!char.IsLetterOrDigit(character))
            {
                startOfWord = true;
                continue;
            }

            result.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = false;
        }

        if (result.Length == 0)
            return "Value";

        // An identifier cannot start with a digit; a status code or a version is the usual culprit.
        return char.IsDigit(result[0]) ? "_" + result : result.ToString();
    }

    /// <summary>
    /// A parameter name: Pascal-cased first, then camel-cased by the same rule the generator applies
    /// to an unnamed <c>[Query]</c>, so whether a key needs spelling out can be decided by comparing.
    /// </summary>
    internal static string Camel(string raw)
        => Escape(QueryNameFormatter.Apply(Pascal(raw), QueryNameFormatter.CamelCase));

    /// <summary>The key the generator derives from an identifier when <c>[Query]</c> names none.</summary>
    internal static string DerivedQueryKey(string identifier)
        => QueryNameFormatter.Apply(identifier.TrimStart('@'), QueryNameFormatter.CamelCase);

    internal static string Escape(string identifier) => Keywords.Contains(identifier) ? "@" + identifier : identifier;

    /// <summary>Returns <paramref name="candidate"/>, numbered if it is already taken, and takes it.</summary>
    internal static string Unique(string candidate, ISet<string> taken)
    {
        var name = candidate;

        for (var suffix = 2; !taken.Add(name); suffix++)
            name = candidate + suffix;

        return name;
    }

    /// <summary>Escapes text for an XML doc comment and keeps it on one line per source line.</summary>
    internal static IEnumerable<string> DocLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var line in text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            yield return line.TrimEnd()
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }
    }

    /// <summary>A C# string literal.</summary>
    internal static string Literal(string value)
        => "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
