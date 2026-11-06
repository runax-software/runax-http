using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Runax.Http.DependencyInjection;

/// <summary>
/// Maps a client interface to the generated implementation that satisfies it.
/// </summary>
/// <remarks>
/// <para>
/// Generated code registers itself here from a <see cref="ModuleInitializerAttribute"/>, which is
/// what lets <c>AddRunaxClient&lt;T&gt;()</c> find an implementation without the application
/// naming it and without reflecting over assemblies. The factory is generated code — a plain
/// <c>new</c> expression — so this works under trimming and native AOT, where scanning for
/// implementations would not.
/// </para>
/// <para>
/// Public because generated code in other assemblies has to reach it, not because applications
/// should. There is nothing here an application needs.
/// </para>
/// </remarks>
public static class GeneratedClientRegistry
{
    private static readonly ConcurrentDictionary<Type, GeneratedClient> Clients = new();

    /// <summary>Registers a generated client implementation.</summary>
    /// <param name="client">The client description.</param>
    /// <remarks>
    /// Last registration wins, and re-registering a contract is not an error. An interface visible
    /// to two compilations legitimately produces two identical registrations, and failing on that
    /// would turn a harmless duplicate into a startup crash.
    /// </remarks>
    public static void Register(GeneratedClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        Clients[client.Contract] = client;
    }

    /// <summary>Finds the generated implementation for a client interface.</summary>
    /// <param name="contract">The client interface.</param>
    /// <param name="client">The implementation description, if one was generated.</param>
    /// <returns><see langword="true"/> if an implementation has been generated for this contract.</returns>
    public static bool TryGet(Type contract, out GeneratedClient? client)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return Clients.TryGetValue(contract, out client);
    }

    /// <summary>
    /// Ensures the module initializers in <paramref name="contract"/>'s assembly have run.
    /// </summary>
    /// <remarks>
    /// A module initializer is guaranteed to run before the first field access or method call in
    /// its module — and <c>typeof(T)</c> is neither. When a client interface lives in an assembly
    /// whose code has not otherwise been touched, which is the normal shape of a dedicated
    /// contracts assembly, registration can run before the generated initializer does. Forcing the
    /// module constructor closes that window at a cost of one call per registration.
    /// </remarks>
    internal static void EnsureInitialized(Type contract)
    {
        try
        {
            RuntimeHelpers.RunModuleConstructor(contract.Module.ModuleHandle);
        }
        catch (Exception exception) when (exception is TypeInitializationException
                                              or MemberAccessException
                                              or NotSupportedException)
        {
            // Nothing useful to do here. If the initializer could not run, the lookup that follows
            // fails with a message about the missing client, which is the actionable one.
        }
    }
}
