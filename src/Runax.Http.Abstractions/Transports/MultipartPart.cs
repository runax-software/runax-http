namespace Runax.Http.Transports;

/// <summary>One part of a <see cref="MultipartFormContent"/> body.</summary>
public sealed class MultipartPart
{
    /// <summary>Initializes a new instance of the <see cref="MultipartPart"/> class.</summary>
    /// <param name="name">The form field name.</param>
    /// <param name="content">The part's content.</param>
    /// <param name="fileName">The file name to report, for a file part.</param>
    public MultipartPart(string name, RequestContent content, string? fileName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);

        Name = name;
        Content = content;
        FileName = fileName;
    }

    /// <summary>The form field name.</summary>
    public string Name { get; }

    /// <summary>The part's content. Nested multipart is not supported.</summary>
    public RequestContent Content { get; }

    /// <summary>The file name to report, or <see langword="null"/> for a non-file part.</summary>
    public string? FileName { get; }
}
