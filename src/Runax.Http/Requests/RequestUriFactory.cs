using System.Text;

namespace Runax.Http.Requests;

/// <summary>
/// Builds the absolute request URI that a transport is handed.
/// </summary>
/// <remarks>
/// <para>
/// This is the single most important thing to keep out of transports. HTTP libraries disagree
/// about URL composition in ways that are easy to miss and expensive to debug: whether a leading
/// slash on the relative path discards the base address's path, whether a trailing slash on the
/// base address survives, which characters get escaped in a query value. Left to each transport,
/// the same client interface would hit different URLs depending on which one was selected — the
/// exact failure this library exists to prevent.
/// </para>
/// <para>
/// So the rules are fixed here, and they are the ones that surprise people least:
/// </para>
/// <list type="bullet">
/// <item>
/// The template always <em>appends</em> to the base address's path. A base of
/// <c>https://host/api/v2</c> with a template of <c>/users</c> gives
/// <c>https://host/api/v2/users</c> — not <c>https://host/users</c>, which is what
/// <see cref="Uri"/>'s own relative resolution would produce and which silently drops the part
/// of the address someone deliberately configured.
/// </item>
/// <item>Slashes at the join are normalized, so a trailing or leading one never doubles up.</item>
/// <item>
/// Placeholder values are escaped as data, so a path argument containing <c>/</c> or <c>?</c>
/// stays one segment instead of changing the shape of the URL.
/// </item>
/// </list>
/// </remarks>
internal static class RequestUriFactory
{
    internal static Uri Create(Uri? baseAddress, ClientRequest request)
    {
        var path = SubstitutePlaceholders(request.Template, request.PathValues);
        var query = BuildQuery(request.QueryValues);

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return Append(absolute, query);

        if (baseAddress is null)
            throw new InvalidOperationException(
                $"The request path '{request.Template}' is relative, but no base address is configured for this client. "
                + "Set one with [BaseAddress] on the interface or WithBaseAddress(...) at registration.");

        if (!baseAddress.IsAbsoluteUri)
            throw new InvalidOperationException(
                $"The configured base address '{baseAddress}' must be absolute.");

        // A template may carry its own query string — [Get("/search?scope=all")] is a reasonable
        // thing to write — and it has to come off before the path is joined. Assigning a string
        // containing '?' to UriBuilder.Path does not split it: the query is escaped into the path
        // or dropped, and either way the request goes somewhere the author did not write.
        var (pathOnly, templateQuery) = SplitQuery(path);

        var combined = new UriBuilder(baseAddress) { Path = Join(baseAddress.AbsolutePath, pathOnly) };

        return Append(Append(combined.Uri, templateQuery), query);
    }

    /// <summary>Separates a relative path from any query string it already carries.</summary>
    private static (string Path, string? Query) SplitQuery(string path)
    {
        var separator = path.IndexOf('?');

        return separator < 0
            ? (path, null)
            : (path.Substring(0, separator), path.Substring(separator + 1));
    }

    /// <summary>
    /// Joins the base address's path with the request path, collapsing the slashes at the seam so
    /// neither a trailing slash on one nor a leading slash on the other changes the result.
    /// </summary>
    private static string Join(string basePath, string path)
    {
        var left = basePath.AsSpan().TrimEnd('/');
        var right = path.AsSpan().TrimStart('/');

        if (right.IsEmpty)
            return left.IsEmpty ? "/" : left.ToString();

        return string.Concat(left, "/", right);
    }

    private static Uri Append(Uri uri, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return uri;

        // A template may carry its own query string. Appending with & rather than replacing keeps
        // both, which is what someone writing [Get("/search?scope=all")] intends.
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');

        builder.Query = existing.Length == 0 ? query : $"{existing}&{query}";

        return builder.Uri;
    }

    private static string SubstitutePlaceholders(
        string template,
        IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        if (template.IndexOf('{') < 0)
            return template;

        var result = new StringBuilder(template.Length);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            var close = template.IndexOf('}', open);
            if (close < 0)
                throw new InvalidOperationException(
                    $"The path template '{template}' has an unclosed '{{' placeholder.");

            result.Append(template, index, open - index);

            var name = template.Substring(open + 1, close - open - 1);
            result.Append(Uri.EscapeDataString(Resolve(template, name, values)));

            index = close + 1;
        }

        return result.ToString();
    }

    private static string Resolve(
        string template,
        string name,
        IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        foreach (var candidate in values)
        {
            if (!string.Equals(candidate.Key, name, StringComparison.Ordinal))
                continue;

            // A null path argument would silently collapse the segment and send the request to a
            // different resource than the one the caller named, so it is refused outright. The
            // generator rejects a placeholder with no parameter at all at compile time; this is
            // the runtime half, for a parameter that is there but null.
            return RequestValueFormatter.Format(candidate.Value)
                   ?? throw new ArgumentException(
                       $"The path parameter '{name}' in template '{template}' was null. "
                       + "A null path value would change which resource is addressed.",
                       name);
        }

        throw new InvalidOperationException(
            $"The path template '{template}' contains the placeholder '{{{name}}}', "
            + "but no path parameter with that name was supplied.");
    }

    private static string? BuildQuery(IReadOnlyList<KeyValuePair<string, object?>> values)
    {
        if (values.Count == 0)
            return null;

        var query = new StringBuilder();

        foreach (var (key, value) in values)
        {
            // A collection argument becomes one key=value pair per item, which is how nearly every
            // API spells a repeated parameter. A null argument contributes nothing at all rather
            // than an empty value, so an unset optional filter is absent instead of being sent as
            // "give me the ones matching nothing".
            foreach (var formatted in RequestValueFormatter.Expand(value))
            {
                if (query.Length > 0)
                    query.Append('&');

                query.Append(Uri.EscapeDataString(key))
                     .Append('=')
                     .Append(Uri.EscapeDataString(formatted));
            }
        }

        return query.Length == 0 ? null : query.ToString();
    }
}
