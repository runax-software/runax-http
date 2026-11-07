namespace Runax.Http.Tests.Contracts;

/// <summary>A successful payload.</summary>
internal sealed record Widget(int Id, string Name);

/// <summary>The shape a well-behaved API returns with a 422.</summary>
internal sealed record ValidationProblem(string[] Errors);

/// <summary>The shape a well-behaved API returns with a 404.</summary>
internal sealed record NotFound(string Message);

/// <summary>The error envelope an API that always answers 200 puts in the body instead.</summary>
internal sealed record ApiError(string Error, int Code);

/// <summary>An envelope with a boolean flag rather than an error property.</summary>
internal sealed record Envelope(bool Success, string? Status, Widget? Data);

/// <summary>
/// The two cases a single return type cannot express: an API with a different shape per status
/// code, and an API that answers 200 whatever happened and puts the outcome in the body.
/// </summary>
[BaseAddress("https://api.example.com")]
internal interface IQuirkyClient
{
    /// <summary>Different shape per status code — the well-behaved version of the problem.</summary>
    [Get("/widgets/{id}")]
    [Response(200, typeof(Widget))]
    [Response(404, typeof(NotFound))]
    [Response(422, typeof(ValidationProblem))]
    Task<ApiResult> GetAsync(int id, CancellationToken ct = default);

    /// <summary>A whole class of status codes at once.</summary>
    [Get("/widgets/{id}/history")]
    [Response(200, typeof(Widget))]
    [Response(typeof(ApiError), MinStatusCode = 500, MaxStatusCode = 599)]
    Task<ApiResult> GetHistoryAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Always 200, with the outcome in the body. A status code cannot tell these apart, so the
    /// discriminator is a property of the body.
    /// </summary>
    [Get("/legacy/widgets/{id}")]
    [Response(typeof(ApiError), WhenJsonProperty = "error")]
    [Response(typeof(Widget))]
    Task<ApiResult> GetLegacyAsync(int id, CancellationToken ct = default);

    /// <summary>Always 200, with a flag rather than the presence of a property.</summary>
    [Get("/legacy/envelope/{id}")]
    [Response(typeof(ApiError), WhenJsonProperty = "success", WhenJsonPropertyEquals = "false")]
    [Response(typeof(Envelope))]
    Task<ApiResult> GetEnvelopeAsync(int id, CancellationToken ct = default);

    /// <summary>Discriminating on a header, for an API that signals the shape out of band.</summary>
    [Get("/legacy/header/{id}")]
    [Response(typeof(ApiError), WhenHeader = "X-Error")]
    [Response(typeof(Widget))]
    Task<ApiResult> GetByHeaderAsync(int id, CancellationToken ct = default);

    /// <summary>A status and a body condition together, which are ANDed.</summary>
    [Get("/combined/{id}")]
    [Response(200, typeof(ApiError), WhenJsonProperty = "error")]
    [Response(200, typeof(Widget))]
    Task<ApiResult> GetCombinedAsync(int id, CancellationToken ct = default);

    /// <summary>No fallback, so an undeclared response comes back unmatched rather than guessed at.</summary>
    [Get("/strict/{id}")]
    [Response(200, typeof(Widget))]
    Task<ApiResult> GetStrictAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// The two body formats that are built from the argument's own shape rather than serialized.
/// </summary>
[BaseAddress("https://api.example.com")]
internal interface IFormClient
{
    [Post("/form")]
    Task<Widget> SubmitAsync(
        [Body(Format = BodyFormat.UrlEncoded)] Dictionary<string, string> fields,
        CancellationToken ct = default);

    [Post("/form/typed")]
    Task<Widget> SubmitTypedAsync(
        [Body(Format = BodyFormat.UrlEncoded)] IEnumerable<KeyValuePair<string, object?>> fields,
        CancellationToken ct = default);

    [Post("/upload")]
    Task<Widget> UploadAsync(
        [Body(Format = BodyFormat.Multipart)] IReadOnlyList<Runax.Http.Transports.MultipartPart> parts,
        CancellationToken ct = default);
}
