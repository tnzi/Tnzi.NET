namespace Tnzi.AI.Skills;

/// <summary>
/// SKILL.md 文件解析器 - 将 Markdown 内容解析为 <see cref="SkillDefinition"/>。
/// </summary>
/// <remarks>
/// 支持 YAML frontmatter 格式（--- 分隔的 key: value 块），后跟 Markdown 正文。
/// 正文中仅解析 ## Requirements 章节（用于运行时依赖验证）。
/// 其余章节（When to Use、Knowledge 等）作为 Content 整体返回，不单独解析为字段。
/// </remarks>
public static partial class SkillMarkdownParser
{
    // 正则表达式 - all use [GeneratedRegex] for source-generated performance

    [GeneratedRegex(@"^##\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"^\s*-\s*(bins|envs|configs|os|toolGroups):\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex RequirementRegex();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugInvalidCharsRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugCollapseHyphensRegex();

    /// <summary>
    /// Parse SKILL.md content into a <see cref="SkillDefinition"/>.
    /// Requires YAML frontmatter (--- delimited key-value block) at the start.
    /// </summary>
    public static SkillDefinition? Parse(string content, string filePath)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        // 解析 frontmatter
        var frontmatter = ParseFrontmatter(content, out var bodyStartIndex);
        if (frontmatter == null)
            return null;

        // 安全验证: frontmatter 键白名单
        var fmValidation = SkillInstallationValidator.ValidateFrontmatter(frontmatter);
        if (!fmValidation.IsValid)
            return null;

        // 安全验证: name 格式（如果存在 slug 字段，验证其格式）
        if (frontmatter.TryGetValue("slug", out var slugValue) && !string.IsNullOrWhiteSpace(slugValue))
        {
            var nameValidation = SkillInstallationValidator.ValidateName(slugValue);
            if (!nameValidation.IsValid)
                return null;
        }

        // 安全验证: description 安全性（拒绝 HTML/XML 注入）
        if (frontmatter.TryGetValue("description", out var descValue) && !string.IsNullOrWhiteSpace(descValue))
        {
            var descValidation = SkillInstallationValidator.ValidateDescription(descValue);
            if (!descValidation.IsValid)
                return null;
        }

        var body = bodyStartIndex < content.Length
            ? content[bodyStartIndex..].TrimStart('\r', '\n')
            : string.Empty;

        var skill = new SkillDefinition
        {
            Content = content,
            FilePath = filePath,
            Scope = SkillScope.System,
            Source = SkillSource.FileSystem
        };

        // 从 frontmatter 填充字段
        ApplyFrontmatter(skill, frontmatter);

        // Name 回退到目录名
        if (string.IsNullOrWhiteSpace(skill.Name))
            skill.Name = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";

        // Slug 回退到自动生成
        if (string.IsNullOrWhiteSpace(skill.Slug))
            skill.Slug = GenerateSlug(skill.Name);

        // 从正文解析 Requirements 章节（框架特有的依赖验证）
        var sections = ParseSections(body);

        if (sections.TryGetValue("requirements", out var requirements))
            skill.Requirements = ParseRequirements(requirements);

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
    // Frontmatter parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// 解析 YAML frontmatter（--- 分隔的 key: value 块）。
    /// </summary>
    /// <param name="content">完整文件内容</param>
    /// <param name="bodyStartIndex">正文起始位置（closing --- 之后）</param>
    /// <returns>解析的 key-value 字典，若无有效 frontmatter 则返回 null</returns>
    internal static Dictionary<string, string>? ParseFrontmatter(string content, out int bodyStartIndex)
    {
        bodyStartIndex = 0;

        // 第一行必须是 ---（兼容 \n 和 \r\n）
        var firstNewline = content.IndexOf('\n');
        if (firstNewline < 0)
            return null;

        var firstLine = content[..firstNewline].Trim();
        if (firstLine != "---")
            return null;

        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 从第一行之后逐行扫描，使用 IndexOf 定位避免 CRLF 偏移问题
        var pos = firstNewline + 1;
        while (pos < content.Length)
        {
            var lineEnd = content.IndexOf('\n', pos);
            // 最后一行可能没有换行符
            if (lineEnd < 0)
                lineEnd = content.Length;

            var line = content[pos..lineEnd].TrimEnd('\r');
            var trimmed = line.Trim();

            // 找到 closing ---
            if (trimmed == "---")
            {
                bodyStartIndex = Math.Min(lineEnd + 1, content.Length);
                return frontmatter;
            }

            pos = Math.Min(lineEnd + 1, content.Length);

            // 跳过空行和注释行
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            // 解析 key: value
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = trimmed[..colonIndex].Trim();
                var value = trimmed[(colonIndex + 1)..].Trim();
                frontmatter[key] = value;
            }
        }

        return null; // 未找到 closing ---
    }

    /// <summary>
    /// 将 frontmatter key-value 映射到 SkillDefinition 字段。
    /// </summary>
    private static void ApplyFrontmatter(SkillDefinition skill, Dictionary<string, string> frontmatter)
    {
        if (frontmatter.TryGetValue("name", out var name))
            skill.Name = name;

        if (frontmatter.TryGetValue("slug", out var slug) && slug.Length <= 64 && !SlugInvalidCharsRegex().IsMatch(slug))
            skill.Slug = slug;

        if (frontmatter.TryGetValue("description", out var description))
            skill.Description = description;

        // `whenToUse` is a routing-signal hint distinct from `description`:
        // description is the human-readable summary, whenToUse is a pushy
        // "trigger phrase" list aimed at the LLM router. Accept kebab-case,
        // camelCase, and snake_case for ergonomic frontmatter authoring.
        if (frontmatter.TryGetValue("when-to-use", out var whenToUseKebab))
            skill.WhenToUse = whenToUseKebab;
        else if (frontmatter.TryGetValue("whenToUse", out var whenToUseCamel))
            skill.WhenToUse = whenToUseCamel;
        else if (frontmatter.TryGetValue("when_to_use", out var whenToUseSnake))
            skill.WhenToUse = whenToUseSnake;

        if (frontmatter.TryGetValue("version", out var version))
            skill.Version = version;

        if (frontmatter.TryGetValue("author", out var author))
            skill.Author = author;

        if (frontmatter.TryGetValue("tags", out var tags))
            skill.Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (frontmatter.TryGetValue("priority", out var priority) && int.TryParse(priority, out var priorityValue))
            skill.Priority = priorityValue;

        if (frontmatter.TryGetValue("enabled", out var enabledStr) && bool.TryParse(enabledStr, out var enabledValue))
            skill.Enabled = enabledValue;

        if (frontmatter.TryGetValue("allowed-tool-groups", out var toolGroups))
            skill.AllowedToolGroups = toolGroups.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (frontmatter.TryGetValue("tool-whitelist", out var toolWhitelist))
            skill.AllowedTools = toolWhitelist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        else if (frontmatter.TryGetValue("allowed-tools", out var allowedTools))
            skill.AllowedTools = allowedTools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (frontmatter.TryGetValue("tool-blacklist", out var toolBlacklist))
            skill.DeniedTools = toolBlacklist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        else if (frontmatter.TryGetValue("denied-tools", out var deniedTools))
            skill.DeniedTools = deniedTools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (frontmatter.TryGetValue("model", out var model))
            skill.RequiredModel = model;

        if (frontmatter.TryGetValue("provider", out var provider))
            skill.RequiredProvider = provider;

        if (frontmatter.TryGetValue("reasoning-effort", out var reasoningEffort))
            skill.RequiredReasoningEffort = reasoningEffort;

        if (frontmatter.TryGetValue("execution-context", out var executionContext)
            && Enum.TryParse<SkillExecutionContext>(executionContext, ignoreCase: true, out var parsedContext))
            skill.ExecutionContext = parsedContext;

        if (frontmatter.TryGetValue("internal", out var internalStr) && bool.TryParse(internalStr, out var internalValue))
            skill.IsInternal = internalValue;

        if (frontmatter.TryGetValue("agents", out var agents))
            skill.Agents = agents.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (frontmatter.TryGetValue("paths", out var paths))
            skill.Paths = paths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
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
}
