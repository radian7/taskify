using System.Text.Json;
using Json.Schema;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Infrastructure;

/// <summary>
/// Loads <c>contracts/events.asyncapi.yaml</c> and checks JSON against the payload schema of a named message (research
/// R15; constitution Quality Gates: contract tests). The AsyncAPI document is authoritative for every event, hub
/// message and hub method argument, so a record that drifts from it fails a test.
/// </summary>
public static class AsyncApiSchemas
{
    private static readonly Lazy<Loaded> Document = new(Load);

    /// <summary>Gets the names of every message in the document (for example <c>TaskMoved</c> or <c>JoinProject</c>).</summary>
    public static IReadOnlyCollection<string> MessageNames => Document.Value.Payloads.Keys;

    /// <summary>Builds the JSON Schema for one message payload, with local <c>$ref</c>s resolved.</summary>
    /// <param name="messageName">The message name under <c>components.messages</c>.</param>
    /// <returns>The schema.</returns>
    public static JsonSchema For(string messageName)
    {
        var loaded = Document.Value;
        if (!loaded.Payloads.TryGetValue(messageName, out var payloadReference))
        {
            throw new ArgumentException($"The AsyncAPI document has no message '{messageName}'.", nameof(messageName));
        }

        // Wrap the components so "$ref: '#/components/schemas/...'" resolves inside the payload schema.
        var wrapper = $$"""
            { "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$ref": "{{payloadReference}}",
              "components": {{loaded.Components}} }
            """;
        return JsonSchema.FromText(wrapper);
    }

    /// <summary>Checks that JSON matches the payload schema of a message. Formats such as <c>uuid</c> are enforced.</summary>
    /// <param name="messageName">The message name.</param>
    /// <param name="json">The JSON to check (an object, or a bare string for a hub method argument).</param>
    /// <returns>The outcome; <c>IsValid</c> is true when the JSON matches.</returns>
    public static EvaluationResults Validate(string messageName, string json)
    {
        using var instance = JsonDocument.Parse(json);
        return For(messageName).Evaluate(
            instance.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,

                // Without this, "format: uuid" and "format: date-time" are only annotations and anything would pass.
                RequireFormatValidation = true,
            });
    }

    private static Loaded Load()
    {
        using var yaml = OpenApiContract.LoadYamlAsJson("events.asyncapi.yaml");
        var components = yaml.RootElement.GetProperty("components");

        var payloads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in components.GetProperty("messages").EnumerateObject())
        {
            // Every message payload in the document is a local reference to a schema.
            payloads[message.Name] = message.Value.GetProperty("payload").GetProperty("$ref").GetString()!;
        }

        return new Loaded(components.GetRawText(), payloads);
    }

    private sealed record Loaded(string Components, Dictionary<string, string> Payloads);
}
