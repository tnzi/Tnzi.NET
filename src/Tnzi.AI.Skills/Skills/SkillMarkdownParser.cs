namespace Tnzi.AI.Skills;

/// <summary>
/// SKILL.md 文件解析器 — 将 Markdown 内容解析为 <see cref="SkillDefinition"/>。
/// </summary>
/// <remarks>
/// 纯静态无状态类，从 FileSystemSkillStore 提取。
/// 支持的 SKILL.md 格式：标题、描述、When to Use、Parameters、Requirements、Metadata。
/// </remarks>
public static partial class SkillMarkdownParser
{
    // 正则表达式 — all use [GeneratedRegex] for source-generated performance

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^##\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"^\s*-\s*(bins|envs|configs|os|toolGroups):\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex RequirementRegex();

    [GeneratedRegex(@"^\s*-\s*(?<name>\w[\w\-]*)\s*:\s*(?<desc>[^(]+?)(?:\s*\((?<opts>[^)]*)\))?\s*$")]
    private static partial Regex ParameterLineRegex();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugInvalidCharsRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugCollapseHyphensRegex();

    [GeneratedRegex(@"allowed:\s*([^,)]+(?:,\s*[^,)]+)*?)(?:\s*,\s*default:|$|\))", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedValuesRegex();

    [GeneratedRegex(@"default:\s*(.+?)(?:\s*,|\s*$|\))", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultValueRegex();

    /// <summary>
    /// Parse SKILL.md content into a <see cref="SkillDefinition"/>.
    /// </summary>
    public static SkillDefinition? Parse(string content, string filePath)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var skill = new SkillDefinition
        {
            Content = content,
            FilePath = filePath,
            Scope = SkillScope.System,
            Source = SkillSource.FileSystem
        };

        // 解析标题
        var titleMatch = TitleRegex().Match(content);
        skill.Name = titleMatch.Success
            ? titleMatch.Groups[1].Value.Trim()
            : Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";

        // 生成 slug
        skill.Slug = GenerateSlug(skill.Name);

        // 解析各章节
        var sections = ParseSections(content);

        // 描述
        skill.Description = ExtractDescription(content, titleMatch);

        // 使用场景
        if (sections.TryGetValue("when to use", out var whenToUse))
            skill.WhenToUse = whenToUse.Trim();

        // 参数
        if (sections.TryGetValue("parameters", out var parametersSection))
            skill.Parameters = ParseParameters(parametersSection);

        // 依赖要求
        if (sections.TryGetValue("requirements", out var requirements))
            skill.Requirements = ParseRequirements(requirements);

        // 元数据
        if (sections.TryGetValue("metadata", out var metadata))
            skill.Metadata = ParseMetadata(metadata);

        // 从元数据提取标准字段
        ApplyMetadataToSkill(skill);

        return skill;
    }

    /// <summary>
    /// 从技能名称生成 kebab-case slug。
    /// </summary>
    public static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unknown";

        var slug = name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        slug = SlugInvalidCharsRegex().Replace(slug, string.Empty);
        slug = SlugCollapseHyphensRegex().Replace(slug, "-");
        slug = slug.Trim('-');

        if (slug.Length > 64)
            slug = slug[..64].TrimEnd('-');

        return string.IsNullOrEmpty(slug) ? "unknown" : slug;
    }

    // -------------------------------------------------------------------------
    // Section parsing
    // -------------------------------------------------------------------------

    private static Dictionary<string, string> ParseSections(string content)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = SectionRegex().Matches(content);

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var sectionName = match.Groups[1].Value.Trim().ToLowerInvariant();
            var startIndex = match.Index + match.Length;
            var endIndex = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var sectionContent = content.Substring(startIndex, endIndex - startIndex).Trim();
            sections[sectionName] = sectionContent;
        }

        return sections;
    }

    private static string? ExtractDescription(string content, Match titleMatch)
    {
        if (!titleMatch.Success)
            return null;

        var startIndex = titleMatch.Index + titleMatch.Length;
        var firstSectionMatch = SectionRegex().Match(content, startIndex);
        var endIndex = firstSectionMatch.Success ? firstSectionMatch.Index : content.Length;
        var description = content.Substring(startIndex, endIndex - startIndex).Trim();
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    /// <summary>
    /// 解析 ## Parameters 章节。
    /// </summary>
    internal static List<SkillParameter> ParseParameters(string section)
    {
        var parameters = new List<SkillParameter>();
        var lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var match = ParameterLineRegex().Match(line);
            if (!match.Success)
                continue;

            var param = new SkillParameter
            {
                Name = match.Groups["name"].Value.Trim(),
                Description = match.Groups["desc"].Value.Trim()
            };

            var opts = match.Groups["opts"].Value;
            if (!string.IsNullOrWhiteSpace(opts))
            {
                param.Required = opts.Contains("required", StringComparison.OrdinalIgnoreCase)
                    && !opts.Contains("optional", StringComparison.OrdinalIgnoreCase);

                var allowedMatch = AllowedValuesRegex().Match(opts);
                if (allowedMatch.Success)
                {
                    param.AllowedValues = allowedMatch.Groups[1].Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                }

                var defaultMatch = DefaultValueRegex().Match(opts);
                if (defaultMatch.Success)
                    param.DefaultValue = defaultMatch.Groups[1].Value.Trim();
            }

            parameters.Add(param);
        }

        return parameters;
    }

    internal static SkillRequirements ParseRequirements(string requirementsSection)
    {
        var requirements = new SkillRequirements();
        var matches = RequirementRegex().Matches(requirementsSection);

        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value.ToLowerInvariant();
            var values = match.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            switch (key)
            {
                case "bins": requirements.Bins = values; break;
                case "envs": requirements.Envs = values; break;
                case "configs": requirements.Configs = values; break;
                case "os": requirements.Os = values; break;
                case "toolgroups": requirements.ToolGroups = values; break;
            }
        }

        return requirements;
    }

    internal static Dictionary<string, string> ParseMetadata(string metadataSection)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = metadataSection.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('-'))
                trimmed = trimmed[1..].Trim();

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = trimmed[..colonIndex].Trim();
                var value = trimmed[(colonIndex + 1)..].Trim();
                metadata[key] = value;
            }
        }

        return metadata;
    }

    // -------------------------------------------------------------------------
    // Metadata → SkillDefinition fields
    // -------------------------------------------------------------------------

    private static void ApplyMetadataToSkill(SkillDefinition skill)
    {
        if (skill.Metadata.TryGetValue("version", out var version))
            skill.Version = version;

        if (skill.Metadata.TryGetValue("author", out var author))
            skill.Author = author;

        if (skill.Metadata.TryGetValue("tags", out var tags))
            skill.Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (skill.Metadata.TryGetValue("priority", out var priority) && int.TryParse(priority, out var priorityValue))
            skill.Priority = priorityValue;

        if (skill.Metadata.TryGetValue("enabled", out var enabledStr) && bool.TryParse(enabledStr, out var enabledValue))
            skill.Enabled = enabledValue;

        // Constraint: tool groups
        if (skill.Metadata.TryGetValue("allowed-tool-groups", out var toolGroups))
        {
            skill.AllowedToolGroups = toolGroups
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        else if (skill.Metadata.TryGetValue("allowed-tools", out var legacyToolGroups))
        {
            // Backward compatibility: allowed-tools as tool group alias when allowed-tool-groups absent
            skill.AllowedToolGroups = legacyToolGroups
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // Constraint: individual tool whitelist
        // Priority: tool-whitelist > allowed-tools (when allowed-tool-groups exists)
        if (skill.Metadata.TryGetValue("tool-whitelist", out var toolWhitelist))
        {
            skill.AllowedTools = toolWhitelist
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        else if (skill.Metadata.ContainsKey("allowed-tool-groups") && skill.Metadata.TryGetValue("allowed-tools", out var individualTools))
        {
            // Backward compat: allowed-tools as individual whitelist when allowed-tool-groups exists
            skill.AllowedTools = individualTools
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // Constraint: individual tool blacklist
        // Priority: tool-blacklist > denied-tools
        if (skill.Metadata.TryGetValue("tool-blacklist", out var toolBlacklist))
        {
            skill.DeniedTools = toolBlacklist
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        else if (skill.Metadata.TryGetValue("denied-tools", out var deniedTools))
        {
            skill.DeniedTools = deniedTools
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        if (skill.Metadata.TryGetValue("model", out var model))
            skill.RequiredModel = model;

        if (skill.Metadata.TryGetValue("provider", out var provider))
            skill.RequiredProvider = provider;
    }
}
