namespace Runax.Http;

/// <summary>
/// Maps one shape of response to the type it should be deserialized into.
/// </summary>
/// <remarks>
/// <para>
/// For the common case a method's return type says everything: <c>Task&lt;Repository&gt;</c> means
/// "deserialize the body into a Repository, throw if the status says no". This attribute is for
/// the two cases where that is not enough.
/// </para>
/// <para>
/// <strong>An API that answers with a different shape per status code.</strong> Declare one per
/// outcome and return <see cref="ApiResult"/>:
/// </para>
/// <code>
/// [Get("/repos/{owner}/{name}")]
/// [Response(200, typeof(Repository))]
/// [Response(404, typeof(NotFound))]
/// [Response(422, typeof(ValidationProblem))]
/// Task&lt;ApiResult&gt; GetAsync(string owner, string name, CancellationToken ct = default);
/// </code>
/// <para>
/// <strong>An API that answers 200 whatever happened</strong> and puts the outcome in the body.
/// This is common, it is not going to be fixed, and a status code cannot tell these apart — so
/// discriminate on the body or a header instead:
/// </para>
/// <code>
/// [Get("/thing/{id}")]
/// [Response(typeof(ApiError), WhenJsonProperty = "error")]
/// [Response(typeof(ApiError), WhenJsonProperty = "status", WhenJsonPropertyEquals = "failed")]
/// [Response(typeof(Thing))]                                  // whatever is left
/// Task&lt;ApiResult&gt; GetAsync(int id, CancellationToken ct = default);
/// </code>
/// <para>
/// <strong>Order decides.</strong> The first attribute whose conditions all hold wins, in the
/// order they are written. That is deliberate: a specificity rule would mean working out which of
/// two overlapping conditions the library considers narrower, and getting it wrong silently. One
/// with no conditions at all matches anything, so it belongs last — anything after it is
/// unreachable, and the generator says so.
/// </para>
/// <para>
/// Choosing the type by inspecting the body means the body has to be buffered, unlike the
/// <c>Task&lt;T&gt;</c> path which can stream it. For an API that needs this, that is not the
/// expensive part of your day.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ResponseAttribute : Attribute
{
    /// <summary>
    /// Matches any response. Put it last, as the fallback.
    /// </summary>
    /// <param name="responseType">The type to deserialize the body into.</param>
    public ResponseAttribute(Type responseType) => ResponseType = responseType;

    /// <summary>Matches one status code exactly.</summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="responseType">The type to deserialize the body into.</param>
    public ResponseAttribute(int statusCode, Type responseType)
    {
        StatusCode = statusCode;
        ResponseType = responseType;
    }

    /// <summary>The type the body is deserialized into when this response matches.</summary>
    public Type ResponseType { get; }

    /// <summary>The exact status code this matches, or <c>0</c> when it does not test the status.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// The low end of a status range, inclusive. Set with <see cref="MaxStatusCode"/> to match a
    /// class of codes — <c>500</c> to <c>599</c> for "anything the server blamed on itself".
    /// </summary>
    public int MinStatusCode { get; set; }

    /// <summary>The high end of a status range, inclusive.</summary>
    public int MaxStatusCode { get; set; }

    /// <summary>
    /// Matches when the body is a JSON object with this property at the top level, whatever its
    /// value. The usual way to spot an error envelope from an API that always answers 200.
    /// </summary>
    public string? WhenJsonProperty { get; set; }

    /// <summary>
    /// Narrows <see cref="WhenJsonProperty"/> to a particular value, compared as text and
    /// case-sensitively. Numbers and booleans compare against their JSON spelling, so
    /// <c>"false"</c> matches <c>"success": false</c>.
    /// </summary>
    public string? WhenJsonPropertyEquals { get; set; }

    /// <summary>Matches when the response carries this header, whatever its value.</summary>
    public string? WhenHeader { get; set; }

    /// <summary>
    /// Narrows <see cref="WhenHeader"/> to a particular value, compared case-insensitively as
    /// header values conventionally are.
    /// </summary>
    public string? WhenHeaderEquals { get; set; }
}
