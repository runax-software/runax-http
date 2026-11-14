using System.Text;

namespace Runax.Http.OpenApi.Tests;

/// <summary>
/// The conversion rules the Bookshop fixture does not reach, the input formats, and the command line.
/// </summary>
public class ConverterTests
{
    [Fact]
    public async Task A_Swagger_2_document_in_JSON_converts_with_its_host_and_base_path()
    {
        var result = await Convert("""
            {
              "swagger": "2.0",
              "info": { "title": "Legacy Inventory", "version": "1" },
              "host": "inventory.example.com",
              "basePath": "/api",
              "schemes": ["https"],
              "paths": {
                "/items/{sku}": {
                  "get": {
                    "operationId": "getItem",
                    "produces": ["application/json"],
                    "parameters": [{ "name": "sku", "in": "path", "required": true, "type": "string" }],
                    "responses": { "200": { "description": "ok", "schema": { "$ref": "#/definitions/Item" } } }
                  }
                }
              },
              "definitions": {
                "Item": { "type": "object", "properties": { "sku": { "type": "string" }, "count": { "type": "integer" } } }
              }
            }
            """);

        var client = result.Files.Single(file => file.Name == "ILegacyInventoryClient.cs").Content;

        client.ShouldContain("""[BaseAddress("https://inventory.example.com/api")]""");
        client.ShouldContain("Task<Item> GetItemAsync(");
        client.ShouldContain("string sku,");
        result.Files.Single(file => file.Name == "LegacyInventoryModels.cs").Content.ShouldContain("public int? Count { get; init; }");
    }

    [Fact]
    public async Task OpenAPI_3_1_nullability_and_alternatives_map_to_nullable_types_and_JsonElement()
    {
        var result = await Convert("""
            openapi: 3.1.0
            info: { title: Shapes, version: '1' }
            servers: [{ url: 'https://shapes.example.com' }]
            paths: {}
            components:
              schemas:
                Thing:
                  type: object
                  required: [label, parent, payload]
                  properties:
                    label: { type: [string, 'null'] }
                    parent: { anyOf: [{ $ref: '#/components/schemas/Thing' }, { type: 'null' }] }
                    payload: { oneOf: [{ type: string }, { type: integer }] }
            """);

        var models = result.Files.Single(file => file.Name == "ShapesModels.cs").Content;

        models.ShouldContain("public required string? Label { get; init; }");
        models.ShouldContain("public required Thing? Parent { get; init; }");
        models.ShouldContain("public required JsonElement Payload { get; init; }");
    }

    [Fact]
    public async Task A_schema_named_like_a_type_the_code_refers_to_is_renamed_rather_than_shadowing_it()
    {
        var result = await Convert("""
            openapi: 3.0.3
            info: { title: Jobs, version: '1' }
            servers: [{ url: 'https://jobs.example.com' }]
            paths:
              /tasks:
                get:
                  operationId: listTasks
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/Task' }
            components:
              schemas:
                Task: { type: object, properties: { id: { type: string } } }
            """);

        result.Files.Single(file => file.Name == "IJobsClient.cs").Content.ShouldContain("Task<Task2> ListTasksAsync(");
    }

    [Fact]
    public async Task A_document_without_a_usable_server_says_so_instead_of_inventing_an_address()
    {
        var result = await Convert("""
            openapi: 3.0.3
            info: { title: Relative, version: '1' }
            servers: [{ url: /v1 }]
            paths: {}
            """);

        result.Files.Single().Content.ShouldNotContain("[BaseAddress");
        result.Warnings.ShouldContain(warning => warning.Contains("relative", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"openapi\": \"3.0.3\"}", "json")]
    [InlineData("﻿  \n{ }", "json")]
    [InlineData("openapi: 3.0.3", "yaml")]
    [InlineData("", "yaml")]
    public void The_format_is_detected_from_the_content(string content, string expected)
        => SpecLoader.DetectFormat(Encoding.UTF8.GetBytes(content)).ShouldBe(expected);

    [Theory]
    [InlineData("pet_id", "PetId", "petId")]
    [InlineData("X-Request-ID", "XRequestID", "xRequestID")]
    [InlineData("class", "Class", "@class")]
    [InlineData("2fa", "_2fa", "_2fa")]
    [InlineData("---", "Value", "value")]
    public void Names_become_identifiers(string raw, string pascal, string camel)
    {
        Identifiers.Pascal(raw).ShouldBe(pascal);
        Identifiers.Camel(raw).ShouldBe(camel);
    }

    [Fact]
    public async Task A_relative_server_resolves_against_the_URL_the_document_came_from()
    {
        var loaded = await SpecLoader.ReadAsync(
            Encoding.UTF8.GetBytes("openapi: 3.0.3\ninfo: { title: Served, version: '1' }\nservers: [{ url: /api/v3 }]\npaths: {}"),
            CancellationToken.None);

        var result = ClientGenerator.Generate(
            loaded.Document!,
            new GeneratorOptions(null, null, false, "openapi.json", new Uri("https://shop.example.com/docs/openapi.json")));

        result.Files.Single().Content.ShouldContain("""[BaseAddress("https://shop.example.com/api/v3")]""");
        result.Warnings.ShouldNotContain(warning => warning.Contains("relative", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_command_writes_the_files_and_refuses_to_overwrite_them_without_force()
    {
        var output = Directory.CreateTempSubdirectory("runax-openapi-");
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bookshop.yaml");

        try
        {
            var (code, stdout, _) = await Run("openapi", fixture, "-o", output.FullName, "-n", "Shop");

            code.ShouldBe(Cli.Success);
            stdout.ShouldContain("services.AddRunaxClient<Shop.IBookshopClient>();");
            Directory.GetFiles(output.FullName).Length.ShouldBe(3);

            var edited = Path.Combine(output.FullName, "IBookshopClient.cs");
            await File.WriteAllTextAsync(edited, "// edited by hand");

            var (refused, _, stderr) = await Run("openapi", fixture, "-o", output.FullName);

            refused.ShouldBe(Cli.Failure);
            stderr.ShouldContain("--force");
            (await File.ReadAllTextAsync(edited)).ShouldBe("// edited by hand");

            (await Run("openapi", fixture, "-o", output.FullName, "--force")).Code.ShouldBe(Cli.Success);
            (await File.ReadAllTextAsync(edited)).ShouldNotBe("// edited by hand");
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(new string[0], Cli.UsageError)]
    [InlineData(new[] { "--help" }, Cli.Success)]
    [InlineData(new[] { "generate" }, Cli.UsageError)]
    [InlineData(new[] { "openapi" }, Cli.UsageError)]
    [InlineData(new[] { "openapi", "a.yaml", "b.yaml" }, Cli.UsageError)]
    [InlineData(new[] { "openapi", "a.yaml", "--output" }, Cli.UsageError)]
    [InlineData(new[] { "openapi", "a.yaml", "--unknown" }, Cli.UsageError)]
    [InlineData(new[] { "openapi", "does-not-exist.yaml" }, Cli.Failure)]
    public async Task Bad_command_lines_fail_with_a_usage_error_or_a_failure(string[] args, int expected)
        => (await Run(args)).Code.ShouldBe(expected);

    private static async Task<GenerationResult> Convert(string document)
    {
        var loaded = await SpecLoader.ReadAsync(Encoding.UTF8.GetBytes(document), CancellationToken.None);

        loaded.Document.ShouldNotBeNull(string.Join(Environment.NewLine, loaded.Problems));

        return ClientGenerator.Generate(loaded.Document, new GeneratorOptions(null, null, ApiResult: false, "test.yaml"));
    }

    private static async Task<(int Code, string Output, string Error)> Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var http = new HttpClient();

        var code = await Cli.RunAsync(args, output, error, http, CancellationToken.None);

        return (code, output.ToString(), error.ToString());
    }
}
