
namespace Tnzi.Audit.Services;

/// <summary>
/// Redacts sensitive fields from JSON request body content.
/// Thread-safe and stateless — safe for singleton registration.
/// </summary>
public class RequestBodyRedactor
{
    private const string RedactedValue = "***REDACTED***";

    /// <summary>
    /// Redact sensitive field values in a JSON string.
    /// Returns the original string if it is not valid JSON.
    /// </summary>
    /// <param name="json">Raw JSON request body</param>
    /// <param name="sensitiveFields">Set of field names to redact (case-insensitive)</param>
    /// <returns>JSON string with sensitive values replaced</returns>
    public string Redact(string json, HashSet<string> sensitiveFields)
    {
        Check.NotNullOrWhiteSpace(json);
        Check.NotNull(sensitiveFields);

        if (sensitiveFields.Count == 0)
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                RedactElement(writer, document.RootElement, sensitiveFields);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // Not valid JSON, return as-is
            return json;
        }
    }

    private static void RedactElement(Utf8JsonWriter writer, JsonElement element, HashSet<string> sensitiveFields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveField(property.Name, sensitiveFields))
                    {
                        writer.WriteStringValue(RedactedValue);
                    }
                    else
                    {
                        RedactElement(writer, property.Value, sensitiveFields);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    RedactElement(writer, item, sensitiveFields);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveField(string fieldName, HashSet<string> sensitiveFields)
    {
        return sensitiveFields.Contains(fieldName);
    }
}
