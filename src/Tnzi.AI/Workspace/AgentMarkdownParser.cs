namespace Tnzi.AI.Workspace;

/// <summary>
/// Static parser for AGENT.md and PERSONA.md workspace files.
/// Splits YAML frontmatter (between --- delimiters) from markdown body.
/// </summary>
public static class AgentMarkdownParser
{
    /// <summary>
    /// Parse an AGENT.md file content into a WorkspaceAgentDefinition.
    /// Returns null if no frontmatter found, empty content, or missing 'name' field.
    /// </summary>
    /// <param name="content">Raw file content</param>
    /// <param name="agentId">Agent identifier (typically the directory name)</param>
    public static WorkspaceAgentDefinition? Parse(string content, string agentId)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var (frontmatter, body) = SplitFrontmatter(content);
        if (frontmatter == null)
        {
            return null;
        }

        var dict = ParseYamlDictionary(frontmatter);

        if (!dict.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new WorkspaceAgentDefinition
        {
            AgentId = agentId,
            Name = name.Trim(),
            Description = dict.GetValueOrDefault("description")?.Trim(),
            Provider = dict.GetValueOrDefault("provider")?.Trim(),
            Model = dict.GetValueOrDefault("model")?.Trim(),
            Temperature = dict.TryGetValue("temperature", out var tempStr) && float.TryParse(tempStr, CultureInfo.InvariantCulture, out var temp) ? temp : null,
            MaxTokens = dict.TryGetValue("maxtokens", out var maxStr) && int.TryParse(maxStr, CultureInfo.InvariantCulture, out var maxTokens) ? maxTokens : null,
            ToolGroups = ParseList(dict.GetValueOrDefault("toolgroups")),
            ExecutionMode = dict.GetValueOrDefault("executionmode")?.Trim(),
            Domains = ParseList(dict.GetValueOrDefault("domains")),
            Roles = ParseList(dict.GetValueOrDefault("roles")),
            QualityTier = dict.TryGetValue("qualitytier", out var qtStr) && int.TryParse(qtStr, CultureInfo.InvariantCulture, out var qt) ? qt : null,
            Instructions = string.IsNullOrWhiteSpace(body) ? null : body.Trim()
        };
    }

    /// <summary>
    /// Parse a PERSONA.md file content into a WorkspacePersonaDefinition.
    /// Returns null if no frontmatter found, empty content, or missing 'name' field.
    /// </summary>
    public static WorkspacePersonaDefinition? ParsePersona(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var (frontmatter, body) = SplitFrontmatter(content);
        if (frontmatter == null)
        {
            return null;
        }

        var dict = ParseYamlDictionary(frontmatter);

        if (!dict.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new WorkspacePersonaDefinition
        {
            Name = name.Trim(),
            Tone = dict.GetValueOrDefault("tone")?.Trim(),
            Language = dict.GetValueOrDefault("language")?.Trim(),
            Content = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim()
        };
    }

    /// <summary>
    /// Split content into YAML frontmatter and markdown body.
    /// Frontmatter is delimited by --- on its own line at the start.
    /// </summary>
    private static (string? Frontmatter, string? Body) SplitFrontmatter(string content)
    {
        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith("---", StringComparison.Ordinal))
        {
            return (null, null);
        }

        // Find the closing ---
        var endIndex = trimmed.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return (null, null);
        }

        var frontmatter = trimmed.Substring(3, endIndex - 3).Trim();
        var bodyStart = endIndex + 4; // skip \n---
        var body = bodyStart < trimmed.Length ? trimmed[bodyStart..] : null;

        return (frontmatter, body);
    }

    /// <summary>
    /// Parse simple YAML key-value pairs into a dictionary.
    /// Handles both inline lists [a, b] and multi-line lists (- item).
    /// Keys are stored lowercase for case-insensitive matching.
    /// </summary>
    private static Dictionary<string, string> ParseYamlDictionary(string yaml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = yaml.Split('\n');
        string? currentKey = null;
        var listItems = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Check for list item continuation (- value)
            var trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("- ", StringComparison.Ordinal) && currentKey != null)
            {
                listItems.Add(trimmedLine[2..].Trim());
                continue;
            }

            // Flush any pending list items
            if (currentKey != null && listItems.Count > 0)
            {
                result[currentKey] = string.Join(", ", listItems);
                listItems.Clear();
                currentKey = null;
            }

            // Parse key: value
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
            {
                // Could be start of a multi-line list
                currentKey = key;
                continue;
            }

            currentKey = null;
            result[key] = value;
        }

        // Flush final list items
        if (currentKey != null && listItems.Count > 0)
        {
            result[currentKey] = string.Join(", ", listItems);
        }

        return result;
    }

    /// <summary>
    /// Parse a list value from either inline format [a, b] or comma-separated string.
    /// Returns null if the value is null or empty.
    /// </summary>
    private static List<string>? ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        // Handle inline list format: [a, b, c]
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        var items = trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return items.Count > 0 ? items : null;
    }
}
