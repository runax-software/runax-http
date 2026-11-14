using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Runax.Http.Proxy;

/// <summary>
/// Implements a client interface at run time by routing every call to its <see cref="ProxyMethod"/>.
/// </summary>
/// <remarks>
/// Not sealed because it cannot be: <see cref="DispatchProxy"/> emits a type deriving from this one.
/// It holds no logic of its own — the plan and the executor do the work, so this is the only part
/// of the proxy path that actually depends on emitting code.
/// </remarks>
[RequiresUnreferencedCode(ProxyContract.TrimmingMessage)]
[RequiresDynamicCode(ProxyContract.AotMessage)]
[SuppressMessage(
    "Performance",
    "CA1852:Seal internal types",
    Justification = "DispatchProxy derives the implementation type from this one.")]
internal class RunaxDispatchProxy : DispatchProxy
{
    private ProxyContract _contract = null!;
    private IHttpContractExecutor _executor = null!;

    internal static object Create(ProxyContract contract, IHttpContractExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        var proxy = (RunaxDispatchProxy)Create(contract.Contract, typeof(RunaxDispatchProxy));

        proxy._contract = contract;
        proxy._executor = executor;

        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        return _contract.Find(targetMethod).Invoke(_executor, args ?? []);
    }
}
