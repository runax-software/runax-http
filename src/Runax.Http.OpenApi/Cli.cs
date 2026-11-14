namespace Runax.Http.OpenApi;

/// <summary>The command line: <c>dotnet runax-http openapi &lt;document&gt; [options]</c>.</summary>
/// <remarks>
/// Hand-rolled rather than built on a parsing library: one command and five options do not need
/// one, and a tool's dependencies are installed on every machine that runs it.
/// </remarks>
internal static class Cli
{
    internal const int Success = 0;
    internal const int UsageError = 1;
    internal const int Failure = 2;

    internal const string Usage = """
        Generates a Runax.Http client interface, its models and a JsonSerializerContext from an OpenAPI
        document. The files are a starting point you own and edit, not a build step.

        Usage:
          dotnet runax-http openapi <document> [options]

        Arguments:
          <document>             A file path or an http(s) URL. JSON or YAML; Swagger 2.0, OpenAPI 3.0 or 3.1.

        Options:
          -o, --output <dir>     Where to write the files. Default: the current directory.
          -n, --namespace <ns>   The namespace. Default: derived from the document's title.
          --name <name>          The client's name: --name Petstore gives IPetstoreClient. Default: from the title.
          --api-result           Return Task<ApiResult> with a [Response] per documented response, instead
                                 of the success response's type.
          --force                Overwrite files that already exist.
          -h, --help             Show this help.
        """;

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            return args.Length == 0 ? UsageError : Success;
        }

        if (args[0] != "openapi")
        {
            await error.WriteLineAsync($"Unknown command '{args[0]}'.{Environment.NewLine}{Environment.NewLine}{Usage}").ConfigureAwait(false);
            return UsageError;
        }

        if (!TryParse(args.AsSpan(1), out var parsed, out var problem))
        {
            await error.WriteLineAsync($"{problem}{Environment.NewLine}{Environment.NewLine}{Usage}").ConfigureAwait(false);
            return UsageError;
        }

        if (parsed.Help)
        {
            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            return Success;
        }

        LoadResult loaded;

        try
        {
            loaded = await SpecLoader.LoadAsync(parsed.Document!, http, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or HttpRequestException or TaskCanceledException)
        {
            await error.WriteLineAsync($"Could not read '{parsed.Document}': {exception.Message}").ConfigureAwait(false);
            return Failure;
        }

        foreach (var reported in loaded.Problems)
            await error.WriteLineAsync($"warning: {reported}").ConfigureAwait(false);

        if (loaded.Document is null)
        {
            await error.WriteLineAsync($"'{parsed.Document}' is not an OpenAPI document that could be read.").ConfigureAwait(false);
            return Failure;
        }

        var result = ClientGenerator.Generate(
            loaded.Document,
            new GeneratorOptions(
                parsed.Namespace,
                parsed.Name,
                parsed.ApiResult,
                parsed.Document!,
                Uri.TryCreate(parsed.Document, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null));

        var directory = Path.GetFullPath(parsed.Output ?? ".");
        var targets = result.Files.Select(file => (File: file, Path: Path.Combine(directory, file.Name))).ToList();

        // Checked for every file before writing any, so a refusal leaves nothing half-replaced.
        var existing = targets.Where(target => File.Exists(target.Path)).Select(target => target.Path).ToList();

        if (existing.Count > 0 && !parsed.Force)
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

    internal sealed class Options
    {
        internal string? Document { get; set; }

        internal string? Output { get; set; }

        internal string? Namespace { get; set; }

        internal string? Name { get; set; }

        internal bool ApiResult { get; set; }

        internal bool Force { get; set; }

        internal bool Help { get; set; }
    }

    internal static bool TryParse(ReadOnlySpan<string> args, out Options options, out string? problem)
    {
        options = new Options();
        problem = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h" or "--help":
                    options.Help = true;
                    return true;

                case "--api-result":
                    options.ApiResult = true;
                    continue;

                case "--force":
                    options.Force = true;
                    continue;

                case "-o" or "--output" or "-n" or "--namespace" or "--name":
                    if (i + 1 >= args.Length)
                    {
                        problem = $"{arg} needs a value.";
                        return false;
                    }

                    var value = args[++i];

                    if (arg is "-o" or "--output")
                        options.Output = value;
                    else if (arg is "-n" or "--namespace")
                        options.Namespace = value;
                    else
                        options.Name = value;

                    continue;
            }

            if (arg.StartsWith('-'))
            {
                problem = $"Unknown option '{arg}'.";
                return false;
            }

            if (options.Document is not null)
            {
                problem = $"Only one document can be converted at a time; '{arg}' is a second.";
                return false;
            }

            options.Document = arg;
        }

        if (options.Document is null)
        {
            problem = "Name the OpenAPI document to convert: a file path or a URL.";
            return false;
        }

        return true;
    }
}
