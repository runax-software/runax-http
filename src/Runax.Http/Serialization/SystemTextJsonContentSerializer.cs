using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Runax.Http.Transports;

namespace Runax.Http.Serialization;

/// <summary>
/// An <see cref="IContentSerializer"/> over <c>System.Text.Json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Serializes through <see cref="JsonTypeInfo"/> rather than the reflection-based
/// <see cref="JsonSerializer"/> overloads, so this type itself is trim- and AOT-safe. Whether
/// <em>your</em> application is depends on the options you hand it: options whose
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> is a source-generated
/// <c>JsonSerializerContext</c> need no reflection at all, which is why that is the constructor
/// without a warning on it.
/// </para>
/// <para>
/// <see cref="CreateReflectionBased"/> is the convenient path and is marked as requiring
/// reflection, because it does. The warning is the point: it is the difference between finding
/// out at build time and finding out when a trimmed deployment starts throwing
/// <c>NotSupportedException</c> on its first request.
/// </para>
/// <para>Instances are thread-safe once constructed.</para>
/// </remarks>
public sealed class SystemTextJsonContentSerializer : IContentSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonContentSerializer"/> class.
    /// </summary>
    /// <param name="options">
    /// The serializer options. Must have a <see cref="JsonSerializerOptions.TypeInfoResolver"/> —
    /// normally a source-generated <c>JsonSerializerContext</c>.
    /// </param>
    /// <param name="contentType">The media type to send and accept.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> has no type-info resolver, so nothing could be serialized
    /// without falling back to reflection.
    /// </exception>
    public SystemTextJsonContentSerializer(
        JsonSerializerOptions options,
        string contentType = "application/json")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(contentType);

        if (options.TypeInfoResolver is null)
            throw new ArgumentException(
                "The JSON options have no TypeInfoResolver. Set one to a source-generated "
                + "JsonSerializerContext, or call SystemTextJsonContentSerializer.CreateReflectionBased() "
                + "to accept the reflection-based resolver and the trimming warning that comes with it.",
                nameof(options));

        // Freezing now means a configuration mistake surfaces at registration rather than on the
        // first request, and removes any question of two clients racing to configure one instance.
        options.MakeReadOnly();

        _options = options;
        ContentType = contentType;
    }

    /// <inheritdoc />
    public string ContentType { get; }

    /// <summary>
    /// Creates a serializer that discovers types by reflection, using web conventions —
    /// camelCase names, case-insensitive reads — which is what an HTTP API usually wants.
    /// </summary>
    /// <param name="options">
    /// Options to start from. A missing resolver is populated with the reflection-based one.
    /// </param>
    /// <param name="contentType">The media type to send and accept.</param>
    /// <returns>The serializer.</returns>
    /// <remarks>
    /// Convenient, and not usable under trimming or native AOT. Prefer the constructor with a
    /// source-generated context for anything you intend to publish that way.
    /// </remarks>
    [RequiresUnreferencedCode(
        "JSON serialization without a source-generated context discovers members by reflection, which "
        + "trimming can remove. Pass options with a JsonSerializerContext resolver instead.")]
    [RequiresDynamicCode(
        "JSON serialization without a source-generated context builds converters at run time, which "
        + "native AOT cannot do. Pass options with a JsonSerializerContext resolver instead.")]
    public static SystemTextJsonContentSerializer CreateReflectionBased(
        JsonSerializerOptions? options = null,
        string contentType = "application/json")
    {
        var effective = options is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(options);

        effective.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();

        return new SystemTextJsonContentSerializer(effective, contentType);
    }

    /// <inheritdoc />
    public RequestContent Serialize<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>());

        return RequestContent.FromBytes(bytes, ContentType);
    }

    /// <inheritdoc />
    public async ValueTask<T?> DeserializeAsync<T>(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return await JsonSerializer
            .DeserializeAsync(content, TypeInfo<T>(), cancellationToken)
            .ConfigureAwait(false);
    }

    private JsonTypeInfo<T> TypeInfo<T>()
    {
        var typeInfo = _options.GetTypeInfo(typeof(T));

        return typeInfo as JsonTypeInfo<T>
               ?? throw new InvalidOperationException(
                   $"The configured JSON options resolved {typeof(T)} to {typeInfo.GetType().Name} rather "
                   + $"than JsonTypeInfo<{typeof(T).Name}>. A source-generated context usually has to be "
                   + "extended with [JsonSerializable] for this type.");
    }
}
