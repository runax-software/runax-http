using System.Runtime.CompilerServices;

namespace Runax.Http.OpenApi.Tests;

/// <summary>
/// The checked-in files under Generated/ are what the tool produces for the fixture today.
/// </summary>
/// <remarks>
/// Those files are compiled into this project with the real generators, warnings as errors, so
/// this is what ties "the tool's output compiles and works" to the tool rather than to a copy that
/// once came out of it. After an intended change to the output, regenerate them with
/// <c>RUNAX_UPDATE_SNAPSHOTS=1 dotnet test</c> and review the diff.
/// </remarks>
public class SnapshotTests
{
    public static TheoryData<string, bool> Variants => new()
    {
        { "Bookshop", false },
        { "BookshopResults", true }
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task The_checked_in_output_is_what_the_tool_generates(string name, bool apiResult)
    {
        var result = await Generate("bookshop.yaml", name, apiResult);
        var directory = Path.Combine(SourceDirectory(), "Generated", name);

        if (Environment.GetEnvironmentVariable("RUNAX_UPDATE_SNAPSHOTS") == "1")
        {
            foreach (var file in result.Files)
                await File.WriteAllTextAsync(Path.Combine(directory, file.Name), file.Content);
        }

        Directory.GetFiles(directory).Select(Path.GetFileName).Order()
            .ShouldBe(result.Files.Select(file => file.Name).Order());

        foreach (var file in result.Files)
        {
            var expected = await File.ReadAllTextAsync(Path.Combine(directory, file.Name));
            file.Content.ReplaceLineEndings("\n").ShouldBe(expected.ReplaceLineEndings("\n"), file.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task The_same_document_as_JSON_generates_the_same_code(string name, bool apiResult)
    {
        var yaml = await Generate("bookshop.yaml", name, apiResult);
        var json = await Generate("bookshop.json", name, apiResult);

        json.Files.Select(file => file.Name).ShouldBe(yaml.Files.Select(file => file.Name));

        foreach (var (fromJson, fromYaml) in json.Files.Zip(yaml.Files))
            fromJson.Content.ShouldBe(fromYaml.Content, fromJson.Name);
    }

    [Fact]
    public async Task Reading_the_fixture_reports_no_problems()
    {
        var loaded = await SpecLoader.ReadAsync(
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bookshop.yaml")),
            CancellationToken.None);

        loaded.Problems.ShouldBeEmpty();
    }

    internal static async Task<GenerationResult> Generate(string fixture, string name, bool apiResult)
    {
        var loaded = await SpecLoader.ReadAsync(
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)),
            CancellationToken.None);

        // The source is always named as the YAML file, so the header comment is the same for both.
        return ClientGenerator.Generate(
            loaded.Document!,
            new GeneratorOptions($"Runax.Http.OpenApi.Tests.{name}", name, apiResult, "bookshop.yaml"));
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
