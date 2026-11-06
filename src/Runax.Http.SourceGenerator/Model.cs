using System.Collections.Immutable;

namespace Runax.Http.SourceGenerator;

/// <summary>How a parameter contributes to the request.</summary>
internal enum ParameterBinding
{
    /// <summary>Fills a <c>{placeholder}</c> in the path template.</summary>
    Path,

    /// <summary>Appends a query parameter.</summary>
    Query,

    /// <summary>Supplies the request body.</summary>
    Body,

    /// <summary>Supplies a header value.</summary>
    Header,

    /// <summary>Cancels the request. Not part of the request itself.</summary>
    CancellationToken
}

/// <summary>What the generated method does with one of its parameters.</summary>
internal sealed class ParameterModel
{
    internal ParameterModel(string name, string wireName, ParameterBinding binding, string typeName)
    {
        Name = name;
        WireName = wireName;
        Binding = binding;
        TypeName = typeName;
    }

    /// <summary>The C# parameter name, used when emitting the method signature and body.</summary>
    internal string Name { get; }

    /// <summary>
    /// The name this value travels under: the placeholder, query key or header name. Equal to
    /// <see cref="Name"/> unless the attribute renamed it.
    /// </summary>
    internal string WireName { get; }

    /// <summary>What part of the request this fills.</summary>
    internal ParameterBinding Binding { get; }

    /// <summary>The fully qualified parameter type, as written into the generated signature.</summary>
    internal string TypeName { get; }
}

/// <summary>How the response maps onto the method's declared return type.</summary>
internal enum ResultShape
{
    /// <summary>Returns <c>Task</c>: the response body is not wanted.</summary>
    Empty,

    /// <summary>Returns <c>Task&lt;T&gt;</c>: the body is deserialized, errors throw.</summary>
    Content,

    /// <summary>Returns <c>Task&lt;ApiResponse&gt;</c>: the outcome is a value, errors do not throw.</summary>
    ApiResponse,

    /// <summary>Returns <c>Task&lt;ApiResponse&lt;T&gt;&gt;</c>.</summary>
    ApiResponseOfContent
}

/// <summary>One client method.</summary>
internal sealed class MethodModel
{
    internal MethodModel(
        string name,
        string requestMethod,
        string template,
        string returnTypeName,
        ResultShape shape,
        string? contentTypeName,
        string? transportName,
        ImmutableArray<ParameterModel> parameters,
        ImmutableArray<KeyValuePair<string, string>> constantHeaders,
        string bodyFormat)
    {
        Name = name;
        RequestMethod = requestMethod;
        Template = template;
        ReturnTypeName = returnTypeName;
        Shape = shape;
        ContentTypeName = contentTypeName;
        TransportName = transportName;
        Parameters = parameters;
        ConstantHeaders = constantHeaders;
        BodyFormat = bodyFormat;
    }

    internal string Name { get; }

    /// <summary>The <c>RequestMethod</c> member name, as written into the generated code.</summary>
    internal string RequestMethod { get; }

    internal string Template { get; }

    internal string ReturnTypeName { get; }

    internal ResultShape Shape { get; }

    /// <summary>The deserialized body type, when the shape has one.</summary>
    internal string? ContentTypeName { get; }

    /// <summary>The transport from <c>[Transport]</c> on this method, if any.</summary>
    internal string? TransportName { get; }

    internal ImmutableArray<ParameterModel> Parameters { get; }

    /// <summary>Headers declared with a constant value on the method.</summary>
    internal ImmutableArray<KeyValuePair<string, string>> ConstantHeaders { get; }

    /// <summary>The <c>BodyFormat</c> member name for the body parameter, if there is one.</summary>
    internal string BodyFormat { get; }
}

/// <summary>One client interface, and everything needed to emit its implementation.</summary>
internal sealed class ClientModel
{
    internal ClientModel(
        string contractName,
        string qualifiedContractName,
        string? namespaceName,
        string implementationName,
        string? baseAddress,
        string? transportName,
        ImmutableArray<KeyValuePair<string, string>> defaultHeaders,
        ImmutableArray<MethodModel> methods)
    {
        ContractName = contractName;
        QualifiedContractName = qualifiedContractName;
        NamespaceName = namespaceName;
        ImplementationName = implementationName;
        BaseAddress = baseAddress;
        TransportName = transportName;
        DefaultHeaders = defaultHeaders;
        Methods = methods;
    }

    internal string ContractName { get; }

    internal string QualifiedContractName { get; }

    internal string? NamespaceName { get; }

    /// <summary>
    /// The generated class's name. Derived from the interface so that two interfaces in one
    /// namespace cannot collide, and suffixed so it cannot collide with a hand-written type either.
    /// </summary>
    internal string ImplementationName { get; }

    internal string? BaseAddress { get; }

    internal string? TransportName { get; }

    internal ImmutableArray<KeyValuePair<string, string>> DefaultHeaders { get; }

    internal ImmutableArray<MethodModel> Methods { get; }
}
