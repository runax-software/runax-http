using System.Text;
using System.Text.Json;

namespace Runax.Http.Requests;

/// <summary>
/// A response with its body already read, so the type to deserialize it as can be chosen by
/// looking at it.
/// </summary>
/// <remarks>
/// <para>
/// Generated code gets one of these when a method carries <see cref="ResponseAttribute"/>, tests
/// the conditions in the order they were declared, and asks the executor to deserialize the type
/// that matched. Each branch is statically typed, so choosing at run time costs nothing in
/// reflection.
/// </para>
/// <para>
/// The body is buffered, which the streaming <c>Task&lt;T&gt;</c> path avoids. That is the price
/// of deciding the type from the content, and it is only paid by methods that ask for it.
/// </para>
/// </remarks>
public sealed class ResponseEnvelope
{
    private readonly ReadOnlyMemory<byte> _content;

    /// <summary>Initializes a new instance of the <see cref="ResponseEnvelope"/> class.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="content">The body, already read.</param>
    public ResponseEnvelope(int statusCode, ReadOnlyMemory<byte> content)
    {
        StatusCode = statusCode;
        _content = content;
    }

    /// <summary>The status code.</summary>
    public int StatusCode { get; }

    /// <summary>The reason phrase, where the transport exposed one.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>The response headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>The name of the transport that ran the request.</summary>
    public string? TransportName { get; init; }

    /// <summary>The address that was requested.</summary>
    public Uri? RequestUri { get; init; }

    /// <summary>The method that was issued.</summary>
    public RequestMethod Method { get; init; }

    /// <summary>Whether the status code is in the 2xx range.</summary>
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    /// <summary>The body, as read from the response.</summary>
    public ReadOnlyMemory<byte> Content => _content;

    /// <summary>Whether the status code is exactly this.</summary>
    /// <param name="statusCode">The status code to test.</param>
    /// <returns><see langword="true"/> if it matches.</returns>
    public bool HasStatus(int statusCode) => StatusCode == statusCode;

    /// <summary>Whether the status code falls in this inclusive range.</summary>
    /// <param name="min">The low end, inclusive.</param>
    /// <param name="max">The high end, inclusive.</param>
    /// <returns><see langword="true"/> if it falls inside.</returns>
    public bool HasStatusInRange(int min, int max) => StatusCode >= min && StatusCode <= max;

    /// <summary>Whether the response carries this header.</summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool HasHeader(string name) => FindHeader(name) is not null;

    /// <summary>Whether the response carries this header with this value.</summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    /// <param name="value">The value, matched case-insensitively as header values conventionally are.</param>
    /// <returns><see langword="true"/> if it matches.</returns>
    public bool HeaderEquals(string name, string value)
        => string.Equals(FindHeader(name), value, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the body is a JSON object with this property at the top level, whatever its value.
    /// </summary>
    /// <param name="name">The property name, matched case-sensitively as JSON names are.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool HasJsonProperty(string name) => TryReadJsonProperty(name, out _);

    /// <summary>
    /// Whether the body is a JSON object whose top-level property has this value.
    /// </summary>
    /// <param name="name">The property name, matched case-sensitively.</param>
    /// <param name="value">
    /// The value as text, matched case-sensitively. Numbers and booleans compare against their
    /// JSON spelling, so <c>"false"</c> matches <c>"success": false</c>.
    /// </param>
    /// <returns><see langword="true"/> if it matches.</returns>
    public bool JsonPropertyEquals(string name, string value)
        => TryReadJsonProperty(name, out var actual)
           && string.Equals(actual, value, StringComparison.Ordinal);

    /// <summary>The body as text, for diagnostics.</summary>
    /// <returns>The decoded body, or <see langword="null"/> if there was none.</returns>
    public string? ReadAsString()
        => _content.IsEmpty ? null : Encoding.UTF8.GetString(_content.Span);

    private string? FindHeader(string name)
    {
        foreach (var (key, value) in Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Finds a top-level property and reports its value as text.
    /// </summary>
    /// <remarks>
    /// Read with <see cref="Utf8JsonReader"/> rather than <see cref="JsonDocument"/>: there is no
    /// document to dispose, nothing is allocated for the properties that are skipped, and it works
    /// under native AOT without a resolver. Only the top level is searched — a discriminator
    /// buried inside a nested object is not a discriminator anyone can rely on.
    /// </remarks>
    private bool TryReadJsonProperty(string name, out string? value)
    {
        value = null;

        if (_content.IsEmpty)
            return false;

        try
        {
            var reader = new Utf8JsonReader(_content.Span);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var matches = reader.ValueTextEquals(name);

                if (!reader.Read())
                    return false;

                if (matches)
                {
                    value = ReadScalar(ref reader);
                    return true;
                }

                // Skips the whole value, nested objects and arrays included. A no-op on a scalar,
                // which is why it can be called unconditionally.
                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // The body is not JSON, or not valid JSON. That is an answer — this condition does not
            // match — and not a reason to fail the request with a parse error.
            return false;
        }

        return false;
    }

    private static string? ReadScalar(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),

        // The raw spelling, so "success": false is matched by WhenJsonPropertyEquals = "false"
        // and an integer by its digits, which is what someone writing the attribute expects.
        JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",

        // Present, but with nothing to compare against. WhenJsonProperty still matches; an
        // equality test against a null, object or array does not.
        _ => null
    };
}
