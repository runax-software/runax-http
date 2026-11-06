using Runax.Http.Transports;

namespace Runax.Http.Serialization;

/// <summary>
/// Turns objects into request bodies and response bodies back into objects.
/// </summary>
/// <remarks>
/// <para>
/// Serialization sits above <see cref="IHttpTransport"/>, not inside it. Several HTTP libraries
/// offer their own serializer and it is tempting to use it; doing so would mean the same client
/// interface produced different JSON, and accepted different JSON, depending on which transport
/// happened to be selected. One serializer, chosen by you, for every transport.
/// </para>
/// <para>
/// Implementations must be thread-safe.
/// </para>
/// </remarks>
public interface IContentSerializer
{
    /// <summary>
    /// The media type this serializer produces, sent as the request's <c>Content-Type</c> and
    /// offered in <c>Accept</c>.
    /// </summary>
    string ContentType { get; }

    /// <summary>Serializes a value into a request body.</summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The encoded body.</returns>
    RequestContent Serialize<T>(T value);

    /// <summary>Deserializes a response body.</summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="content">The response body stream. Not disposed by this method.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The deserialized value, or <see langword="null"/> if the body was empty or represented
    /// a null value.
    /// </returns>
    ValueTask<T?> DeserializeAsync<T>(Stream content, CancellationToken cancellationToken = default);
}
