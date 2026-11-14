using System.CommandLine;

namespace Runax.Http.OpenApi;

/// <summary>The command line: <c>dotnet runax-http openapi &lt;document&gt; [options]</c>.</summary>
/// <remarks>
/// Declared with System.CommandLine, which the <c>dotnet</c> CLI is built on, so help, usage
/// errors, response files and Ctrl+C behave the way they do for every other <c>dotnet</c> command.
/// Each command is its symbols plus an action; adding one is adding a <see cref="Command"/>.
/// </remarks>
internal static class Cli
{
    internal const int Success = 0;
    internal const int UsageError = 1;
    internal const int Failure = 2;

    /// <summary>What the <c>openapi</c> command was asked to do.</summary>
    internal sealed record OpenApiRequest(
        string Document,
        string? Output,
        string? Namespace,
        string? Name,
        bool ApiResult,
        bool Force);

    internal static RootCommand Build(HttpClient http)
    {
        var document = new Argument<string>("document")
        {
            Description = "A file path or an http(s) URL. JSON or YAML; Swagger 2.0, OpenAPI 3.0 or 3.1."
        };

        var output = new Option<string?>("--output", "-o")
        {
            Description = "Where to write the files. Default: the current directory.",
            HelpName = "dir"
        };

        var ns = new Option<string?>("--namespace", "-n")
        {
            Description = "The namespace. Default: the client's name.",
            HelpName = "namespace"
        };

        var name = new Option<string?>("--name")
        {
            Description = "The client's name: --name Shop gives IShopClient. Default: from the document's title.",
            HelpName = "name"
        };

        var apiResult = new Option<bool>("--api-result")
        {
            Description = "Return Task<ApiResult> with a [Response] per documented response, instead of the success response's type."
        };

        var force = new Option<bool>("--force")
        {
            Description = "Overwrite files that already exist. They are yours once generated, so this is never the default."
        };

        var openapi = new Command(
            "openapi",
            "Generate a client interface, its models and a JsonSerializerContext from an OpenAPI document. "
            + "The files are a starting point you own and edit, not a build step.")
        {
            document, output, ns, name, apiResult, force
        };

        openapi.SetAction((parse, cancellationToken) => GenerateAsync(
            new OpenApiRequest(
                parse.GetRequiredValue(document),
                parse.GetValue(output),
                parse.GetValue(ns),
                parse.GetValue(name),
                parse.GetValue(apiResult),
                parse.GetValue(force)),
            parse.InvocationConfiguration.Output,
            parse.InvocationConfiguration.Error,
            http,
            cancellationToken));

        return new RootCommand("Tools for Runax.Http, the declarative HTTP client for .NET.") { openapi };
    }

    /// <summary>Parses and runs a command line, writing to the given writers rather than the console.</summary>
    internal static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpClient http,
        CancellationToken cancellationToken)
        => Build(http)
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);

    internal static async Task<int> GenerateAsync(
        OpenApiRequest request,
        TextWriter output,
        TextWriter error,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        LoadResult loaded;

        try
        {
            loaded = await SpecLoader.LoadAsync(request.Document, http, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or HttpRequestException or TaskCanceledException)
        {
            await error.WriteLineAsync($"Could not read '{request.Document}': {exception.Message}").ConfigureAwait(false);
            return Failure;
        }

        foreach (var reported in loaded.Problems)
            await error.WriteLineAsync($"warning: {reported}").ConfigureAwait(false);

        if (loaded.Document is null)
        {
            await error.WriteLineAsync($"'{request.Document}' is not an OpenAPI document that could be read.").ConfigureAwait(false);
            return Failure;
        }

        var result = ClientGenerator.Generate(
            loaded.Document,
            new GeneratorOptions(
                request.Namespace,
                request.Name,
                request.ApiResult,
                request.Document,
                Uri.TryCreate(request.Document, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null));

        var directory = Path.GetFullPath(request.Output ?? ".");
        var targets = result.Files.Select(file => (File: file, Path: Path.Combine(directory, file.Name))).ToList();

        // Checked for every file before writing any, so a refusal leaves nothing half-replaced.
        var existing = targets.Where(target => File.Exists(target.Path)).Select(target => target.Path).ToList();

        if (existing.Count > 0 && !request.Force)
        {
            await error.WriteLineAsync(
                "These files already exist, and may have been edited since they were generated:"
                + string.Concat(existing.Select(path => $"{Environment.NewLine}  {path}"))
                + $"{Environment.NewLine}Pass --force to overwrite them, or --output to write somewhere else.").ConfigureAwait(false);

            return Failure;
        }

        Directory.CreateDirectory(directory);

        foreach (var (file, path) in targets)
        {
            await File.WriteAllTextAsync(path, file.Content, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"  wrote {Path.GetRelativePath(Environment.CurrentDirectory, path)}").ConfigureAwait(false);
        }

        foreach (var warning in result.Warnings)
            await error.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("Register it with:").ConfigureAwait(false);
        await output.WriteLineAsync(result.JsonContextName is { } context
            ? $"  services.AddRunaxHttp().UseSystemTextJson({result.Namespace}.{context}.Default.Options);"
            : "  services.AddRunaxHttp().UseSystemTextJson();").ConfigureAwait(false);
        await output.WriteLineAsync($"  services.AddRunaxClient<{result.Namespace}.{result.InterfaceName}>();").ConfigureAwait(false);

        return Success;
    }
}
