namespace Runax.Http;

/// <summary>How a <see cref="BodyAttribute"/>-annotated parameter is encoded.</summary>
public enum BodyFormat
{
    /// <summary>Serialized by the configured <see cref="Runax.Http.Serialization.IContentSerializer"/>.</summary>
    Json,

    /// <summary><c>application/x-www-form-urlencoded</c>.</summary>
    UrlEncoded,

    /// <summary><c>multipart/form-data</c>.</summary>
    Multipart,

    /// <summary>Sent as-is: <see cref="string"/>, <see cref="byte"/> array, or <see cref="System.IO.Stream"/>.</summary>
    Raw
}
