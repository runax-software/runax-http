using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Runax.Http.DependencyInjection;
using Runax.Http.Requests;
using Runax.Http.Retry;
using Runax.Http.SourceGenerator;
using Runax.Http.Transports;

namespace Runax.Http.Proxy;

/// <summary>
/// A client interface read by reflection, for when no implementation was generated for it.
/// </summary>
/// <remarks>
/// <para>
/// Applies the generator's rules rather than rules of its own: the same binding for an unannotated
/// parameter, the same query shapes and naming, the same body formats, the same response matching.
/// A contract has to mean the same request whichever path implements it — otherwise moving an
/// interface into a project with the generator would quietly change what goes on the wire.
/// </para>
/// <para>
/// The generator's errors become one exception at registration, listing every problem with the
/// generator's <c>RXH</c> id so <c>docs/diagnostics.md</c> explains each. That is later than a
/// build error, but still before the first request: a contract that cannot be implemented never
/// gets as far as a call. The generator's warnings have no equivalent and are not reported.
/// </para>
/// </remarks>
[RequiresUnreferencedCode(TrimmingMessage)]
[RequiresDynamicCode(AotMessage)]
internal sealed partial class ProxyContract
{
    internal const string TrimmingMessage =
        "A runtime proxy reads the client interface, its parameters and its query objects by reflection, "
        + "which trimming can remove. Reference Runax.Http from the project declaring the interface so an "
        + "implementation is generated, and register it with AddRunaxClient<T>().";

    internal const string AotMessage =
        "A runtime proxy emits its implementation type at run time and closes generic helpers over the "
        + "interface's types, which native AOT cannot do. Reference Runax.Http from the project declaring "
        + "the interface so an implementation is generated, and register it with AddRunaxClient<T>().";

    private const string QueryAdvice =
        "A query value has to be something the runtime can turn into text: a string, number, bool, "
        + "enum, date, Guid or Uri, anything IFormattable, a collection of those, a dictionary keyed "
        + "by string, or an object whose properties are all of those.";

    /// <summary>
    /// Plans by contract, so a client registered in several containers — one per test, typically —
    /// is reflected over once.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ProxyContract> Plans = new();

    private readonly Dictionary<MethodInfo, ProxyMethod> _methods;

    private ProxyContract(Type contract, Dictionary<MethodInfo, ProxyMethod> methods)
    {
        Contract = contract;
        _methods = methods;
    }

    internal Type Contract { get; }

    /// <summary>
    /// The same description a generated client registers, so registration, option seeding and
    /// transport validation treat both paths identically.
    /// </summary>
    internal GeneratedClient Description { get; private set; } = null!;

    /// <summary>Plans a contract, or returns the plan made earlier.</summary>
    /// <exception cref="InvalidOperationException">The contract cannot be implemented.</exception>
    internal static ProxyContract For(Type contract) => Plans.GetOrAdd(contract, Plan);

    internal ProxyMethod Find(MethodInfo method)
        => _methods.TryGetValue(method, out var plan)
            ? plan
            : throw new NotSupportedException(
                $"{method.DeclaringType}.{method.Name} is not a request method of {Contract}.");

    [GeneratedRegex(@"\{(?<name>[^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    private static ProxyContract Plan(Type contract)
    {
        var problems = new List<string>();
        var naming = contract.GetCustomAttribute<QueryNamingAttribute>()?.Naming ?? QueryNaming.CamelCase;
        var baseAddress = ReadBaseAddress(contract, problems);
        var methods = new Dictionary<MethodInfo, ProxyMethod>();
        var methodTransports = new List<string>();

        // Inherited interfaces too: a proxy is asked to implement every method the contract
        // exposes, and one it had no plan for would fail on its first call instead of here.
        foreach (var declaring in (Type[])[contract, .. contract.GetInterfaces()])
        {
            foreach (var member in declaring.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                switch (member)
                {
                    // Accessors arrive as methods too; the property or event is what gets reported.
                    case MethodInfo { IsSpecialName: true }:
                        break;

                    case MethodInfo method:
                        if (PlanMethod(method, naming, problems, out var transport) is { } plan)
                        {
                            methods[method] = plan;

                            if (transport is not null)
                                methodTransports.Add(transport);
                        }

                        break;

                    case PropertyInfo or EventInfo:
                        problems.Add(
                            $"RXH013: '{Describe(member)}' is a {(member is PropertyInfo ? "property" : "event")}. "
                            + "A client interface describes requests, and a request is a method call.");

                        break;
                }
            }
        }

        if (methods.Count == 0 && problems.Count == 0)
        {
            problems.Add(
                "it declares no methods. Mark at least one with a request attribute such as [Get] or [Post].");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{contract} cannot be implemented by a runtime proxy:"
                + string.Concat(problems.Select(problem => $"{Environment.NewLine}  {problem}"))
                + $"{Environment.NewLine}The ids are the build errors the source generator reports for the same "
                + "mistakes; docs/diagnostics.md describes each one and its fix.");
        }

        var transportName = contract.GetCustomAttribute<TransportAttribute>()?.Name;
        var proxy = new ProxyContract(contract, methods);

        proxy.Description = new GeneratedClient
        {
            Contract = contract,
            Factory = executor => RunaxDispatchProxy.Create(proxy, executor),
            BaseAddress = baseAddress,
            TransportName = transportName,
            DefaultHeaders = ConstantHeaders(contract)
                .Select(header => new KeyValuePair<string, string>(header.Key, header.Value!))
                .ToArray(),
            DeclaredTransportNames = methodTransports
                .Prepend(transportName)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        return proxy;
    }

    private static ProxyMethod? PlanMethod(
        MethodInfo method,
        QueryNaming naming,
        List<string> problems,
        out string? transportName)
    {
        var name = Describe(method);
        transportName = null;

        if (method.GetCustomAttribute<HttpMethodAttribute>() is not { } request)
        {
            problems.Add(
                $"RXH009: '{name}' has no request attribute. Mark it with [Get], [Post], [Put], [Patch], "
                + "[Delete], [Head] or [Options].");

            return null;
        }

        if (method.IsGenericMethodDefinition)
        {
            problems.Add(
                $"RXH008: '{name}' is generic. The body and response types have to be known when the "
                + "client is built, so a type parameter cannot stand in for them.");

            return null;
        }

        var problemCount = problems.Count;
        var responses = ReadResponses(method, name, problems);
        var dispatch = PlanDispatch(method, name, responses, problems);
        var plan = PlanParameters(method, name, request.Template ?? string.Empty, naming, problems);
        var retry = ReadRetry(method, name, problems);

        if (problems.Count > problemCount || dispatch is null || plan is null)
            return null;

        transportName = method.GetCustomAttribute<TransportAttribute>()?.Name;

        return new ProxyMethod(
            request.Method,
            request.Template ?? string.Empty,
            transportName,
            retry,
            ConstantHeaders(method),
            plan.Path,
            plan.Query,
            plan.Headers,
            plan.Body,
            plan.TokenIndex,
            dispatch);
    }

    /// <summary>Decides how a method's return type is produced, as the generator's result shapes do.</summary>
    private static Func<IHttpContractExecutor, ClientRequest, CancellationToken, object>? PlanDispatch(
        MethodInfo method,
        string name,
        ProxyResponse[] responses,
        List<string> problems)
    {
        var returnType = method.ReturnType;
        var isResult = returnType == typeof(Task<ApiResult>);

        if (responses.Length > 0 && !isResult)
        {
            problems.Add(
                $"RXH015: '{name}' carries [Response] but returns '{returnType}'. A method describing more "
                + "than one response shape has to return Task<ApiResult>.");

            return null;
        }

        if (responses.Length == 0 && isResult)
        {
            problems.Add(
                $"RXH016: '{name}' returns Task<ApiResult> but declares no [Response], so every call would "
                + "come back unmatched.");

            return null;
        }

        if (returnType == typeof(Task))
            return ProxyMethod.SendEmpty;

        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            problems.Add(
                $"RXH002: '{name}' returns '{returnType}'. Use Task, Task<T>, Task<ApiResponse>, "
                + "Task<ApiResponse<T>> or Task<ApiResult>.");

            return null;
        }

        var result = returnType.GetGenericArguments()[0];

        if (isResult)
            return (executor, request, token) => ProxyMethod.SendForResultAsync(executor, request, responses, token);

        if (result == typeof(ApiResponse))
            return ProxyMethod.SendForApiResponse;

        if (result.IsGenericType && result.GetGenericTypeDefinition() == typeof(ApiResponse<>))
            return ProxyMethod.ApiResponseDispatcher(result.GetGenericArguments()[0]);

        return ProxyMethod.ContentDispatcher(result);
    }

    /// <summary>
    /// Reads the <see cref="ResponseAttribute"/>s in declaration order, since the first match wins.
    /// </summary>
    /// <remarks>
    /// Declaration order is the order the compiler writes attributes to metadata, and the order
    /// reflection returns them in. One after an unconditional response can never match; the
    /// generator warns and drops it, and so does this.
    /// </remarks>
    private static ProxyResponse[] ReadResponses(MethodInfo method, string name, List<string> problems)
    {
        var responses = new List<ProxyResponse>();
        var conditions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declared in method.GetCustomAttributes<ResponseAttribute>())
        {
            if (responses.Count > 0 && responses[^1].IsUnconditional)
                break;

            var response = new ProxyResponse(declared);

            if (!conditions.Add(DescribeCondition(response)))
            {
                problems.Add(
                    $"RXH017: '{name}' declares more than one [Response] for {DescribeCondition(response)}. "
                    + "The first one wins and the rest can never run.");

                continue;
            }

            responses.Add(response);
        }

        return [.. responses];
    }

    private static RetryOverride? ReadRetry(MethodInfo method, string name, List<string> problems)
    {
        if (method.GetCustomAttribute<RetryAttribute>() is not { } retry)
            return null;

        if (retry.MaxAttempts < 1)
        {
            problems.Add(
                $"RXH021: '{name}' declares [Retry({retry.MaxAttempts})]. Attempts include the first, so "
                + "the lowest meaningful value is 1.");

            return null;
        }

        return new RetryOverride(retry.MaxAttempts, retry.DelayMilliseconds, retry.AllowNonIdempotent);
    }

    private sealed record ParameterPlan(
        (string Name, int Index)[] Path,
        Func<object?[], IEnumerable<KeyValuePair<string, object?>>>[] Query,
        (string Name, int Index, Func<object?, string?> Format)[] Headers,
        (int Index, Func<IHttpContractExecutor, object?, RequestContent> Create)? Body,
        int TokenIndex);

    private static ParameterPlan? PlanParameters(
        MethodInfo method,
        string name,
        string template,
        QueryNaming naming,
        List<string> problems)
    {
        var placeholders = PlaceholderPattern().Matches(template)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var problemCount = problems.Count;
        var path = new List<(string Name, int Index)>();
        var query = new List<Func<object?[], IEnumerable<KeyValuePair<string, object?>>>>();
        var headers = new List<(string Name, int Index, Func<object?, string?> Format)>();
        (int Index, Func<IHttpContractExecutor, object?, RequestContent> Create)? body = null;
        var tokenIndex = -1;
        var bodies = 0;

        foreach (var parameter in method.GetParameters())
        {
            var index = parameter.Position;
            var parameterName = parameter.Name ?? $"arg{index}";
            var type = parameter.ParameterType;

            if (type == typeof(CancellationToken))
            {
                if (tokenIndex >= 0)
                {
                    problems.Add($"RXH006: '{name}' takes more than one CancellationToken. Keep one, as the last parameter.");
                    continue;
                }

                tokenIndex = index;
                continue;
            }

            // The first binding attribute wins, as it does in the generator.
            var binding = parameter.GetCustomAttributes()
                .FirstOrDefault(attribute => attribute is PathAttribute or QueryAttribute or BodyAttribute or HeaderAttribute);

            switch (binding)
            {
                // An unannotated parameter binds to a placeholder of the same name and to nothing
                // else: guessing that it must be a query parameter is how a secret ends up in a URL.
                case null when placeholders.Contains(parameterName):
                    path.Add((parameterName, index));
                    break;

                case null:
                    problems.Add(
                        $"RXH005: '{parameterName}' on '{name}' is not bound to any part of the request. Mark it "
                        + "with [Query], [Path], [Body] or [Header], or name it after a placeholder in the template.");

                    break;

                case PathAttribute pathAttribute:
                    path.Add((NonEmpty(pathAttribute.Name) ?? parameterName, index));
                    break;

                case HeaderAttribute header:
                    headers.Add((NonEmpty(header.Name) ?? parameterName, index, HeaderValue.Format));
                    break;

                case BodyAttribute bodyAttribute:
                    if (++bodies > 1)
                    {
                        problems.Add($"RXH004: '{name}' marks more than one parameter with [Body]. A request has a single body.");
                        continue;
                    }

                    if (PlanBody(type, bodyAttribute.Format, out var advice) is { } create)
                    {
                        body = (index, create);
                    }
                    else
                    {
                        problems.Add(
                            $"RXH019: '{parameterName}' on '{name}' is typed '{type}', which "
                            + $"BodyFormat.{bodyAttribute.Format} cannot send. {advice}");
                    }

                    break;

                case QueryAttribute queryAttribute:
                    {
                        // A policy applies to a derived name only; an explicit [Query("name")] is the
                        // author stating the key outright and is never transformed.
                        var effective = ExplicitNaming(parameter) ?? naming;
                        var wireName = NonEmpty(queryAttribute.Name) ?? Apply(parameterName, effective);

                        if (PlanQuery(parameter, wireName, effective, name, problems) is { } contributor)
                            query.Add(contributor);

                        break;
                    }
            }
        }

        var bound = path
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var placeholder in placeholders)
        {
            if (!bound.TryGetValue(placeholder, out var count))
            {
                problems.Add(
                    $"RXH003: the template '{template}' contains '{{{placeholder}}}', but '{name}' has no "
                    + $"parameter bound to it. Add a parameter named '{placeholder}', or mark one with "
                    + $"[Path(\"{placeholder}\")].");
            }
            else if (count > 1)
            {
                problems.Add(
                    $"RXH012: '{name}' binds more than one parameter to '{{{placeholder}}}'. Only one value "
                    + "can fill a placeholder.");
            }
        }

        return problems.Count > problemCount
            ? null
            : new ParameterPlan([.. path], [.. query], [.. headers], body, tokenIndex);
    }

    /// <summary>How a body parameter becomes content, or why it cannot.</summary>
    /// <remarks>
    /// The same set the generator accepts. A form or multipart body is built from the argument's own
    /// shape, so a POCO there is refused here as it is at compile time, rather than reflected over —
    /// the proxy reflects over the contract, not over the values passed through it.
    /// </remarks>
    private static Func<IHttpContractExecutor, object?, RequestContent>? PlanBody(
        Type type,
        BodyFormat format,
        out string? advice)
    {
        advice = null;

        switch (format)
        {
            case BodyFormat.UrlEncoded:
                foreach (var element in SequenceElements(type))
                {
                    if (StringKeyedValueType(element) is { } valueType)
                        return ProxyMethod.FormBodyOf(valueType);
                }

                advice = "A form body is a sequence of key/value pairs keyed by string, such as a "
                         + "Dictionary<string, string>. Use BodyFormat.Json for an object.";

                return null;

            case BodyFormat.Multipart:
                if (SequenceElements(type).Contains(typeof(MultipartPart)))
                    return static (_, value) => MultipartBody.Create((IEnumerable<MultipartPart>)value!);

                advice = "A multipart body is a sequence of MultipartPart, built at the call site.";

                return null;

            case BodyFormat.Raw:
                if (type == typeof(string))
                    return static (_, value) => RawBody.Create((string)value!);

                if (type == typeof(byte[]))
                    return static (_, value) => RawBody.Create((byte[])value!);

                if (type == typeof(ReadOnlyMemory<byte>))
                    return static (_, value) => RawBody.Create((ReadOnlyMemory<byte>)value!);

                if (typeof(Stream).IsAssignableFrom(type))
                    return static (_, value) => RawBody.Create((Stream)value!);

                advice = "A raw body is sent as written, so it has to be a string, byte[], "
                         + "ReadOnlyMemory<byte> or Stream.";

                return null;

            default:
                return ProxyMethod.JsonBody(type);
        }
    }

    /// <summary>
    /// How a <c>[Query]</c> parameter becomes query entries: one entry, a dictionary's entries, or
    /// one per property of an object — tested in that order, because a string is a sequence of
    /// characters and a dictionary is a sequence of pairs.
    /// </summary>
    private static Func<object?[], IEnumerable<KeyValuePair<string, object?>>>? PlanQuery(
        ParameterInfo parameter,
        string wireName,
        QueryNaming naming,
        string name,
        List<string> problems)
    {
        var type = parameter.ParameterType;
        var index = parameter.Position;

        if (IsSendable(type))
            return args => [new KeyValuePair<string, object?>(wireName, args[index])];

        var element = SequenceElements(type).FirstOrDefault();

        if (element is not null && StringKeyedValueType(element) is { } valueType)
        {
            var expand = ProxyMethod.DictionaryQueryOf(valueType);
            return args => expand(args[index]);
        }

        if (element is not null)
        {
            if (IsSendable(element))
                return args => [new KeyValuePair<string, object?>(wireName, args[index])];

            problems.Add(
                $"RXH022: '{parameter.Name}' on '{name}' is typed '{type}', whose items are '{element}', "
                + $"which cannot be sent. {QueryAdvice}");

            return null;
        }

        var properties = ReadableProperties(type);

        if (properties.Count == 0)
        {
            problems.Add($"RXH022: '{parameter.Name}' on '{name}' is typed '{type}', which cannot be sent. {QueryAdvice}");
            return null;
        }

        foreach (var property in properties)
        {
            if (IsSendable(property.PropertyType)
                || SequenceElements(property.PropertyType).FirstOrDefault() is { } item && IsSendable(item))
            {
                continue;
            }

            // Nesting is refused rather than flattened: there is no one convention for naming a
            // nested field, so flattening would have to guess.
            problems.Add(
                $"RXH023: '{parameter.Name}' on '{name}' is a query object, but its property '{property.Name}' "
                + $"is typed '{property.PropertyType}', which cannot be sent. "
                + (ReadableProperties(property.PropertyType).Count > 0
                    ? "Flatten the type, or pass the inner object as its own [Query] parameter."
                    : QueryAdvice));

            return null;
        }

        var fields = properties
            .Select(property => (
                WireName: NonEmpty(property.GetCustomAttribute<QueryAttribute>()?.Name)
                          ?? Apply(property.Name, naming),
                Property: property))
            .ToArray();

        // A null object contributes null for every field, and a null query value is omitted — the
        // same outcome the generator's null-conditional access produces.
        return args =>
        {
            var value = args[index];

            return Array.ConvertAll(fields, field => new KeyValuePair<string, object?>(
                field.WireName,
                value is null ? null : field.Property.GetValue(value)));
        };
    }

    /// <summary>
    /// The public, readable, non-indexer instance properties of a type and its bases, most-derived
    /// first, as the generator collects them.
    /// </summary>
    private static List<PropertyInfo> ReadableProperties(Type type)
    {
        var properties = new List<PropertyInfo>();
        var bare = Nullable.GetUnderlyingType(type) ?? type;

        if (bare.IsArray || bare.IsPointer || bare.IsEnum || bare.IsPrimitive)
            return properties;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = bare; current is not null && current != typeof(object); current = current.BaseType)
        {
            var declared = current.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var property in declared)
            {
                if (property.GetIndexParameters().Length == 0
                    && property.GetMethod is { IsPublic: true }
                    && seen.Add(property.Name))
                {
                    properties.Add(property);
                }
            }
        }

        return properties;
    }

    /// <summary>Whether <see cref="RequestValueFormatter"/> can turn this type into text.</summary>
    /// <remarks>Mirrors that formatter's switch, as the generator's check does.</remarks>
    private static bool IsSendable(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;

        return bare.IsEnum
               || bare == typeof(string)
               || bare == typeof(bool)
               || bare == typeof(char)
               || bare == typeof(Uri)
               || typeof(IFormattable).IsAssignableFrom(bare);
    }

    /// <summary>The element types of every <c>IEnumerable&lt;T&gt;</c> a type is or implements, excluding string.</summary>
    private static IEnumerable<Type> SequenceElements(Type type)
    {
        if (type == typeof(string))
            yield break;

        var bare = Nullable.GetUnderlyingType(type) ?? type;

        if (bare.IsGenericType && bare.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            yield return bare.GetGenericArguments()[0];

        foreach (var implemented in bare.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                yield return implemented.GetGenericArguments()[0];
        }
    }

    /// <summary>The value type of a <c>KeyValuePair&lt;string, TValue&gt;</c>, or null for anything else.</summary>
    private static Type? StringKeyedValueType(Type element)
        => element.IsGenericType
           && element.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
           && element.GetGenericArguments()[0] == typeof(string)
            ? element.GetGenericArguments()[1]
            : null;

    /// <summary>
    /// The convention a <c>[Query(Naming = …)]</c> names, or null when it names none and the
    /// interface's policy applies. Read from attribute data, because an attribute instance cannot
    /// tell an explicit <see cref="QueryNaming.CamelCase"/> from the default.
    /// </summary>
    private static QueryNaming? ExplicitNaming(ParameterInfo parameter)
    {
        foreach (var data in parameter.GetCustomAttributesData())
        {
            if (data.AttributeType != typeof(QueryAttribute))
                continue;

            foreach (var argument in data.NamedArguments)
            {
                if (argument.MemberName == nameof(QueryAttribute.Naming) && argument.TypedValue.Value is int value)
                    return (QueryNaming)value;
            }
        }

        return null;
    }

    /// <summary>
    /// Shares the generator's formatter — the same source file, compiled into both — so a key cannot
    /// be spelled differently depending on which path implemented the client.
    /// </summary>
    private static string Apply(string identifier, QueryNaming naming)
        => QueryNameFormatter.Apply(identifier, naming.ToString());

    /// <summary>Headers declared with both a name and a value, which are the ones baked in.</summary>
    private static KeyValuePair<string, string?>[] ConstantHeaders(MemberInfo member)
        => member.GetCustomAttributes<HeaderAttribute>()
            .Where(header => header.Value is not null)
            .Select(header => new KeyValuePair<string, string?>(header.Name, header.Value))
            .ToArray();

    private static string? ReadBaseAddress(Type contract, List<string> problems)
    {
        if (contract.GetCustomAttribute<BaseAddressAttribute>() is not { } declared)
            return null;

        if (string.IsNullOrWhiteSpace(declared.BaseAddress)
            || !Uri.TryCreate(declared.BaseAddress, UriKind.Absolute, out _))
        {
            problems.Add(
                $"RXH011: [BaseAddress(\"{declared.BaseAddress}\")] is not an absolute URI. Include the scheme "
                + "and host, as in \"https://api.example.com\".");

            return null;
        }

        return declared.BaseAddress;
    }

    /// <summary>A response's conditions as text, for duplicate detection — the generator's wording.</summary>
    private static string DescribeCondition(ProxyResponse response)
    {
        if (response.IsUnconditional)
            return "any response";

        var parts = new List<string>();

        if (response.StatusCode is { } status)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"status {status}"));

        if (response.MinStatusCode is not null || response.MaxStatusCode is not null)
        {
            var low = response.MinStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "*";
            var high = response.MaxStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "*";

            parts.Add($"status {low}-{high}");
        }

        if (response.JsonProperty is { } property)
        {
            parts.Add(response.JsonPropertyEquals is { } expected
                ? $"body property '{property}' == '{expected}'"
                : $"body property '{property}'");
        }

        if (response.Header is { } header)
        {
            parts.Add(response.HeaderEquals is { } value
                ? $"header '{header}' == '{value}'"
                : $"header '{header}'");
        }

        return string.Join(" and ", parts);
    }

    private static string Describe(MemberInfo member) => $"{member.DeclaringType?.Name}.{member.Name}";

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
