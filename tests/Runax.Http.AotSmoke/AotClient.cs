using System.Text.Json;
using System.Text.Json.Serialization;
using Runax.Http;

namespace Runax.Http.AotSmoke;

/// <summary>A response type, serialized through a source-generated context.</summary>
internal sealed record Thing(int Id, string Name);

/// <summary>A request body, serialized through a source-generated context.</summary>
internal sealed record NewThing(string Name);

/// <summary>
/// A client interface, so the generator produces an implementation that the AOT compiler then has
/// to accept. Without this the smoke test would only prove the hand-written runtime is AOT-safe,
/// which is the easier half.
/// </summary>
[BaseAddress("https://api.example.com/v1")]
[Header("User-Agent", "runax-http-aot")]
internal interface IThingClient
{
    [Get("/things/{id}")]
    Task<Thing> GetAsync(int id, CancellationToken ct = default);

    [Get("/things")]
    Task<ApiResponse<Thing>> TryGetAsync([Query] string name, CancellationToken ct = default);

    [Post("/things")]
    Task<Thing> CreateAsync([Body] NewThing thing, CancellationToken ct = default);

    [Delete("/things/{id}")]
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// The run-time-dispatched path. Each branch names a concrete type, so this has to stay
    /// reflection-free under AOT too — the decision is dynamic, the deserialization is not.
    /// </summary>
    [Get("/things/{id}/quirky")]
    [Response(typeof(ThingError), WhenJsonProperty = "error")]
    [Response(404, typeof(ThingError))]
    [Response(typeof(Thing))]
    Task<ApiResult> GetQuirkyAsync(int id, CancellationToken ct = default);
}

/// <summary>An error envelope, for the dispatched path.</summary>
internal sealed record ThingError(string Error);

/// <summary>
/// The source-generated JSON contract. This is the configuration that makes serialization
/// reflection-free, and therefore the configuration an AOT application has to use — hence
/// UseSystemTextJson(options) below rather than the convenient parameterless overload, which is
/// marked as requiring reflection precisely so this choice cannot be made by accident.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Thing))]
[JsonSerializable(typeof(NewThing))]
[JsonSerializable(typeof(ThingError))]
internal sealed partial class AotJsonContext : JsonSerializerContext;

internal static class AotJson
{
    internal static JsonSerializerOptions Options => new()
    {
        TypeInfoResolver = AotJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
