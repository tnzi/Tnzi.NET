namespace Tnzi.AI.Skills.Services;

/// <summary>
/// Shared JSON serialization helpers for skill entities and definitions.
/// </summary>
internal static class SkillJsonHelper
{
    public static T? DeserializeOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json, TnziJsonDefaults.Options); }
        catch (JsonException) { return default; }
    }

    public static string SerializeOrDefault<T>(T? value, string defaultJson) where T : class =>
        value != null ? JsonSerializer.Serialize(value, TnziJsonDefaults.Options) : defaultJson;

    public static string? BuildConstraintsJson(
        List<string>? allowedToolGroups,
        List<string>? allowedTools,
        List<string>? deniedTools,
        string? requiredModel,
        string? requiredProvider)
    {
        if (allowedToolGroups == null && allowedTools == null && deniedTools == null
            && requiredModel == null && requiredProvider == null)
            return null;

        var obj = new Dictionary<string, object?>();
        if (allowedToolGroups != null) obj["allowedToolGroups"] = allowedToolGroups;
        if (allowedTools != null) obj["allowedTools"] = allowedTools;
        if (deniedTools != null) obj["deniedTools"] = deniedTools;
        if (requiredModel != null) obj["requiredModel"] = requiredModel;
        if (requiredProvider != null) obj["requiredProvider"] = requiredProvider;

        return JsonSerializer.Serialize(obj, TnziJsonDefaults.Options);
    }

    /// <summary>
    /// Merges non-null input constraint fields with existing constraint JSON, preserving unmodified fields.
    /// </summary>
    public static string? MergeConstraintsJson(
        string? existingJson,
        List<string>? allowedToolGroups,
        List<string>? allowedTools,
        List<string>? deniedTools,
        string? requiredModel,
        string? requiredProvider)
    {
        // If no input provided, keep existing
        if (allowedToolGroups == null && allowedTools == null && deniedTools == null
            && requiredModel == null && requiredProvider == null)
            return existingJson;

        // Read existing values as fallback
        var effectiveGroups = allowedToolGroups ?? ParseConstraintField<List<string>>(existingJson, "allowedToolGroups");
        var effectiveTools = allowedTools ?? ParseConstraintField<List<string>>(existingJson, "allowedTools");
        var effectiveDenied = deniedTools ?? ParseConstraintField<List<string>>(existingJson, "deniedTools");
        var effectiveModel = requiredModel ?? ParseConstraintField<string>(existingJson, "requiredModel");
        var effectiveProvider = requiredProvider ?? ParseConstraintField<string>(existingJson, "requiredProvider");

        return BuildConstraintsJson(effectiveGroups, effectiveTools, effectiveDenied, effectiveModel, effectiveProvider);
    }

    public static T? ParseConstraintField<T>(string? constraintsJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(constraintsJson)) return default;
        try
        {
            using var doc = JsonDocument.Parse(constraintsJson);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop))
                return JsonSerializer.Deserialize<T>(prop.GetRawText(), TnziJsonDefaults.Options);
        }
        catch (JsonException) { }
        return default;
    }
}
