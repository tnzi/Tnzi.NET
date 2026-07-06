namespace Tnzi.Json;

/// <summary>
/// Provides default JSON serialization options for the framework.
/// </summary>
[StableApi(Since = "0.1.0")]
public static class TnziJsonDefaults
{
    /// <summary>
    /// Default JSON serializer options: camelCase naming, no indentation, case-insensitive reading,
    /// enums serialized as PascalCase member names (numeric input still accepted for compatibility).
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
