using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskify.Contracts;

/// <summary>
/// Reads and writes enums as their exact member names and nothing else. Integers, wrong casing and
/// unknown names are rejected with a <see cref="JsonException"/> (allow-list validation, constitution
/// Principle II; the built-in converter is case-insensitive and can accept numbers).
/// </summary>
public sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly Dictionary<string, T> ByName =
            Enum.GetValues<T>().ToDictionary(v => v.ToString(), v => v, StringComparer.Ordinal);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a string value for {typeof(T).Name}.");
            }

            // Deliberately do not echo the rejected value back (it is untrusted input).
            return ByName.TryGetValue(reader.GetString()!, out var value)
                ? value
                : throw new JsonException($"Value is not an allowed {typeof(T).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
