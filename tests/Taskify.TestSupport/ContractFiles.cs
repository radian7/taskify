using System.Text.Json;
using Json.Schema;
using YamlDotNet.Serialization;

namespace Taskify.TestSupport;

/// <summary>
/// Loads the committed contracts from <c>specs/001-taskify-kanban-board/contracts</c> and checks JSON against their
/// schemas, so a response that drifts from the contract fails a test (constitution Quality Gates: contract tests).
/// </summary>
public sealed class OpenApiContract
{
    private readonly JsonElement components;

    private OpenApiContract(JsonElement components) => this.components = components;

    /// <summary>Finds the contracts folder by walking up from the test output folder.</summary>
    /// <returns>The folder path.</returns>
    public static string ContractsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "specs", "001-taskify-kanban-board", "contracts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find specs/001-taskify-kanban-board/contracts above the test folder.");
    }

    /// <summary>Converts a YAML contract file to a JSON document.</summary>
    /// <param name="fileName">The file name inside the contracts folder.</param>
    /// <returns>The parsed document. The caller disposes it.</returns>
    public static JsonDocument LoadYamlAsJson(string fileName)
    {
        var yaml = File.ReadAllText(Path.Combine(ContractsDirectory(), fileName));
        // Without this, YamlDotNet reads every plain scalar as text, so "maxLength: 100" would become "100".
        var data = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build().Deserialize<object>(yaml);
        var json = new SerializerBuilder().JsonCompatible().Build().Serialize(data);
        return JsonDocument.Parse(json);
    }

    /// <summary>Loads an OpenAPI contract.</summary>
    /// <param name="fileName">For example <c>projects-api.yaml</c>.</param>
    /// <returns>The contract.</returns>
    public static OpenApiContract Load(string fileName)
    {
        using var document = LoadYamlAsJson(fileName);
        return new OpenApiContract(document.RootElement.GetProperty("components").Clone());
    }

    /// <summary>Checks that JSON matches a schema from <c>components.schemas</c>.</summary>
    /// <param name="schemaName">The schema name, for example <c>User</c>.</param>
    /// <param name="json">The JSON to check.</param>
    /// <returns>The validation outcome; <c>IsValid</c> is true when the JSON matches.</returns>
    public EvaluationResults Validate(string schemaName, string json)
    {
        // OpenAPI 3.1 schemas are JSON Schema 2020-12. Wrap the components so "$ref: '#/components/...'" resolves.
        var wrapper = $$"""
            { "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$ref": "#/components/schemas/{{schemaName}}",
              "components": {{components.GetRawText()}} }
            """;
        var schema = JsonSchema.FromText(wrapper);
        using var instance = JsonDocument.Parse(json);
        return schema.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    /// <summary>Checks that every item of a JSON array matches a schema. An empty array passes.</summary>
    /// <param name="schemaName">The item schema name.</param>
    /// <param name="json">The JSON array.</param>
    /// <returns>The outcome; <see cref="ContractCheck.IsValid"/> is true when every item matches.</returns>
    public ContractCheck ValidateItems(string schemaName, string json)
    {
        using var document = JsonDocument.Parse(json);
        var index = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var result = Validate(schemaName, item.GetRawText());
            if (!result.IsValid)
            {
                return new ContractCheck(false, $"item {index} does not match {schemaName}: {item.GetRawText()}");
            }

            index++;
        }

        return new ContractCheck(true, null);
    }
}

/// <summary>The outcome of checking a collection against a schema.</summary>
/// <param name="IsValid">Whether every item matched.</param>
/// <param name="Failure">What failed, when something did.</param>
public sealed record ContractCheck(bool IsValid, string? Failure);
