namespace Runax.Http.Tests.Contracts;

/// <summary>
/// A client that declares a transport, so the precedence rules have something to override.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IRepositoryClient"/>. A declared transport that is not
/// registered now fails when transports resolve, so leaving this method on the shared contract
/// would force every test harness to register a "declared" transport it has no interest in — and
/// that pressure to register something just to keep startup quiet is exactly what the validation
/// is meant to surface rather than create.
/// </remarks>
[BaseAddress("https://api.example.com/v3")]
internal interface ITrafficClient
{
    [Get("/repos/{owner}/{name}/traffic")]
    [Transport("declared")]
    Task<Repository> GetTrafficAsync(string owner, string name, CancellationToken ct = default);
}
