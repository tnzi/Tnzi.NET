
namespace Tnzi.AI.Skills;

/// <summary>
/// 技能加载器 - 从文件系统加载 SKILL.md 文件
/// </summary>
/// <remarks>
/// <para>
/// 支持的 SKILL.md 格式：
/// <code>
/// # Skill Name
///
/// Description of the skill...
///
/// ## When to Use
///
/// Description of when to use this skill...
///
/// ## Requirements
///
/// - bins: git, npm
/// - envs: API_KEY
/// - os: windows, linux, macos
/// </code>
/// </para>
/// </remarks>
public class SkillLoader
{
    private readonly ILogger<SkillLoader> _logger;
    private readonly SkillsOptions _options;

    private static readonly Regex TitleRegex = new(@"^#\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex SectionRegex = new(@"^##\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex RequirementRegex = new(@"^\s*-\s*(bins|envs|configs|os|toolGroups):\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SkillLoader(ILogger<SkillLoader> logger, IOptions<AIOptions> options)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value.ContextProviders.Skills;
    }

    /// <summary>
    /// 从配置的路径加载所有技能
    /// </summary>
    public async Task<List<SkillDefinition>> LoadSkillsAsync(CancellationToken ct = default)
    {
        var skills = new List<SkillDefinition>();

        if (_options.Paths.Count == 0)
        {
            _logger.LogDebug("No skill paths configured");
            return skills;
        }

        foreach (var path in _options.Paths)
        {
            try
            {
                var loadedSkills = await LoadSkillsFromPathAsync(path, ct);
                skills.AddRange(loadedSkills);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skills from path: {Path}", path);
            }
        }

        // 应用允许/禁止列表过滤
        skills = FilterSkills(skills);

        _logger.LogInformation("Loaded {Count} skills from {PathCount} paths",
            skills.Count, _options.Paths.Count);

        return skills;
    }

    /// <summary>
    /// 从指定路径加载技能
    /// </summary>
    private async Task<List<SkillDefinition>> LoadSkillsFromPathAsync(string path, CancellationToken ct)
    {
        var skills = new List<SkillDefinition>();

        if (!Directory.Exists(path))
        {
            _logger.LogDebug("Skill path does not exist: {Path}", path);
            return skills;
        }

        // 搜索 SKILL.md 文件
        var skillFiles = Directory.GetFiles(path, "SKILL.md", SearchOption.AllDirectories);

        foreach (var file in skillFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var skill = await LoadSkillFromFileAsync(file, ct);
                if (skill != null)
                {
                    skills.Add(skill);
                    _logger.LogDebug("Loaded skill: {SkillName} from {FilePath}", skill.Name, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skill from file: {FilePath}", file);
            }
        }

        return skills;
    }

    /// <summary>
    /// 从文件加载单个技能
    /// </summary>
    public async Task<SkillDefinition?> LoadSkillFromFileAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(filePath, ct);
        return ParseSkillContent(content, filePath);
    }

    /// <summary>
    /// 解析技能内容
    /// </summary>
    private SkillDefinition? ParseSkillContent(string content, string filePath)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var skill = new SkillDefinition
        {
            Content = content,
            FilePath = filePath,
            Id = GenerateSkillId(filePath)
        };

        // 解析标题
        var titleMatch = TitleRegex.Match(content);
        if (titleMatch.Success)
        {
            skill.Name = titleMatch.Groups[1].Value.Trim();
        }
        else
        {
            // 使用文件夹名作为技能名
            skill.Name = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";
        }

        // 解析各个章节
        var sections = ParseSections(content);

        // 提取描述（标题后到第一个 ## 之间的内容）
        skill.Description = ExtractDescription(content, titleMatch);

        // 提取使用场景
        if (sections.TryGetValue("when to use", out var whenToUse))
        {
            skill.WhenToUse = whenToUse.Trim();
        }

        // 解析依赖要求
        if (sections.TryGetValue("requirements", out var requirements))
        {
            skill.Requirements = ParseRequirements(requirements);
        }

        // 解析元数据
        if (sections.TryGetValue("metadata", out var metadata))
        {
            skill.Metadata = ParseMetadata(metadata);
        }

        // 从元数据中提取常用字段
        if (skill.Metadata.TryGetValue("version", out var version))
        {
            skill.Version = version;
        }
        if (skill.Metadata.TryGetValue("author", out var author))
        {
            skill.Author = author;
        }
        if (skill.Metadata.TryGetValue("tags", out var tags))
        {
            skill.Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        if (skill.Metadata.TryGetValue("priority", out var priority) && int.TryParse(priority, out var priorityValue))
        {
            skill.Priority = priorityValue;
        }

        return skill;
    }

    /// <summary>
    /// 解析章节
    /// </summary>
    private static Dictionary<string, string> ParseSections(string content)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = SectionRegex.Matches(content);

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

    /// <summary>
    /// 提取描述
    /// </summary>
    private static string? ExtractDescription(string content, Match titleMatch)
    {
        if (!titleMatch.Success)
        {
            return null;
        }

        var startIndex = titleMatch.Index + titleMatch.Length;
        var firstSectionMatch = SectionRegex.Match(content, startIndex);
        var endIndex = firstSectionMatch.Success ? firstSectionMatch.Index : content.Length;

        var description = content.Substring(startIndex, endIndex - startIndex).Trim();
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    /// <summary>
    /// 解析依赖要求
    /// </summary>
    private static SkillRequirements ParseRequirements(string requirementsSection)
    {
        var requirements = new SkillRequirements();
        var matches = RequirementRegex.Matches(requirementsSection);

        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value.ToLowerInvariant();
            var values = match.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            switch (key)
            {
                case "bins":
                    requirements.Bins = values;
                    break;
                case "envs":
                    requirements.Envs = values;
                    break;
                case "configs":
                    requirements.Configs = values;
                    break;
                case "os":
                    requirements.Os = values;
                    break;
                case "toolgroups":
                    requirements.ToolGroups = values;
                    break;
            }
        }

        return requirements;
    }

    /// <summary>
    /// 解析元数据
    /// </summary>
    private static Dictionary<string, string> ParseMetadata(string metadataSection)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = metadataSection.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("-"))
            {
                trimmed = trimmed.Substring(1).Trim();
            }

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = trimmed.Substring(0, colonIndex).Trim();
                var value = trimmed.Substring(colonIndex + 1).Trim();
                metadata[key] = value;
            }
        }

        return metadata;
    }

    /// <summary>
    /// 生成技能 ID（使用 SHA256 确定性哈希，跨进程/运行时稳定）
    /// </summary>
    private static string GenerateSkillId(string filePath)
    {
        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        var hashBytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"skill_{Convert.ToHexString(hashBytes)[..16].ToLowerInvariant()}";
    }

    /// <summary>
    /// 应用允许/禁止列表过滤
    /// </summary>
    private List<SkillDefinition> FilterSkills(List<SkillDefinition> skills)
    {
        // 如果有允许列表，只保留列表中的技能
        if (_options.AllowList.Count > 0)
        {
            skills = skills.Where(s =>
                _options.AllowList.Contains(s.Name, StringComparer.OrdinalIgnoreCase) ||
                _options.AllowList.Contains(s.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        // 移除禁止列表中的技能
        if (_options.DenyList.Count > 0)
        {
            skills = skills.Where(s =>
                !_options.DenyList.Contains(s.Name, StringComparer.OrdinalIgnoreCase) &&
                !_options.DenyList.Contains(s.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        return skills;
    }

    /// <summary>
    /// 验证技能依赖要求
    /// </summary>
    public SkillValidationResult ValidateRequirements(SkillDefinition skill)
    {
        if (!_options.RequireChecksEnabled || skill.Requirements == null)
        {
            return new SkillValidationResult { IsValid = true };
        }

        var result = new SkillValidationResult { IsValid = true };

        // 检查可执行文件
        foreach (var bin in skill.Requirements.Bins)
        {
            if (!IsBinaryAvailable(bin))
            {
                result.IsValid = false;
                result.MissingBins.Add(bin);
            }
        }

        // 检查环境变量
        foreach (var env in skill.Requirements.Envs)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
            {
                result.IsValid = false;
                result.MissingEnvs.Add(env);
            }
        }

        // 检查操作系统
        if (skill.Requirements.Os.Count > 0)
        {
            var currentOs = GetCurrentOs();
            if (!skill.Requirements.Os.Contains(currentOs, StringComparer.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.UnsupportedOs = currentOs;
            }
        }

        return result;
    }

    /// <summary>
    /// 检查可执行文件是否可用
    /// </summary>
    private static bool IsBinaryAvailable(string binary)
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            var extensions = OperatingSystem.IsWindows()
                ? new[] { ".exe", ".cmd", ".bat", ".ps1", "" }
                : new[] { "" };

            foreach (var path in paths)
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(path, binary + ext);
                    if (File.Exists(fullPath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取当前操作系统
    /// </summary>
    private static string GetCurrentOs()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "unknown";
    }
}

/// <summary>
/// 技能验证结果
/// </summary>
public class SkillValidationResult
{
    /// <summary>
    /// 验证是否通过
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 缺失的可执行文件
    /// </summary>
    public List<string> MissingBins { get; set; } = [];

    /// <summary>
    /// 缺失的环境变量
    /// </summary>
    public List<string> MissingEnvs { get; set; } = [];

    /// <summary>
    /// 不支持的操作系统
    /// </summary>
    public string? UnsupportedOs { get; set; }

    /// <summary>
    /// 获取验证失败的原因描述
    /// </summary>
    public string GetFailureReason()
    {
        var reasons = new List<string>();

        if (MissingBins.Count > 0)
        {
            reasons.Add($"Missing binaries: {string.Join(", ", MissingBins)}");
        }
        if (MissingEnvs.Count > 0)
        {
            reasons.Add($"Missing environment variables: {string.Join(", ", MissingEnvs)}");
        }
        if (!string.IsNullOrEmpty(UnsupportedOs))
        {
            reasons.Add($"Unsupported OS: {UnsupportedOs}");
        }

        return string.Join("; ", reasons);
    }
}