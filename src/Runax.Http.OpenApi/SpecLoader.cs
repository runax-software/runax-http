using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Runax.Http.OpenApi;

/// <summary>What reading a document produced.</summary>
/// <param name="Document">The document, or null when it could not be read at all.</param>
/// <param name="Problems">What the reader objected to. A document with problems is still used when it parsed.</param>
internal sealed record LoadResult(OpenApiDocument? Document, IReadOnlyList<string> Problems);

/// <summary>Reads an OpenAPI document from a file or a URL, in JSON or YAML.</summary>
internal static class SpecLoader
{
    internal static async Task<LoadResult> LoadAsync(
        string input,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        var bytes = Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? await http.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false)
            : await File.ReadAllBytesAsync(input, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<LoadResult> ReadAsync(byte[] content, CancellationToken cancellationToken)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        using var stream = new MemoryStream(content, writable: false);

        var result = await OpenApiDocument
            .LoadAsync(stream, DetectFormat(content), settings, cancellationToken)
            .ConfigureAwait(false);

        var problems = result.Diagnostic?.Errors
            .Select(error => error.Pointer is { Length: > 0 } pointer ? $"{pointer}: {error.Message}" : error.Message)
            .ToList() ?? [];

        return new LoadResult(result.Document, problems);
    }

    /// <summary>
    /// JSON when the content starts with a brace, YAML otherwise. Decided from the content rather
    /// than a file extension, because a document served from a URL usually has none.
    /// </summary>
    internal static string DetectFormat(ReadOnlySpan<byte> content)
    {
        // A UTF-8 byte order mark, which editors on Windows like to add.
        if (content.StartsWith("﻿"u8))
            content = content[3..];

        foreach (var value in content)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                continue;

            return value == (byte)'{' ? OpenApiConstants.Json : OpenApiConstants.Yaml;
        }

        return OpenApiConstants.Yaml;
    }
}
