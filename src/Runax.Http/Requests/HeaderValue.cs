namespace Runax.Http.Requests;

/// <summary>
/// Formats a header argument, for generated code.
/// </summary>
/// <remarks>
/// Shares <see cref="RequestValueFormatter"/> with path and query values, so the same argument
/// spells the same thing wherever it is bound. A <see cref="DateTimeOffset"/> that goes out as an
/// ISO-8601 query value and as a locale-shaped header would be its own small nightmare to debug.
/// </remarks>
public static class HeaderValue
{
    /// <summary>Formats a value for a header.</summary>
    /// <param name="value">The argument.</param>
    /// <returns>
    /// The formatted value, or <see langword="null"/> to omit the header — which is also how a
    /// method suppresses a client-wide default header.
    /// </returns>
    public static string? Format(object? value) => RequestValueFormatter.Format(value);
}
