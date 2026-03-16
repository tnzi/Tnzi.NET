namespace Tnzi.AI.Skills;

/// <summary>
/// 基于文件系统的技能存储 — 扫描 SKILL.md 文件并解析为 <see cref="SkillDefinition"/>。
/// </summary>
/// <remarks>
/// <para>单例生命周期，内置 TTL 缓存（SemaphoreSlim 双重检查锁）。</para>
/// <para>
/// 支持的 SKILL.md 格式：
/// <code>
/// # Skill Name
///
/// Description of the skill...
///
/// ## When to Use
///
/// Use when...
///
/// ## Parameters
///
/// - name: description (required, allowed: v1, v2, default: v1)
///
/// ## Requirements
///
/// - bins: git, npm
/// - envs: API_KEY
/// - os: windows, linux, macos
///
/// ## Metadata
///
/// - version: 1.0
/// - enabled: false
/// </code>
/// </para>
/// </remarks>
public class FileSystemSkillStore : ISkillStore
{
    private readonly ILogger<FileSystemSkillStore> _logger;
    private readonly SkillsOptions _options;
    private readonly IConfiguration? _configuration;
    private readonly IToolRegistry? _toolRegistry;

    // 缓存状态
    private List<SkillDefinition>? _cache;
    private DateTime _cacheExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // 正则表达式
    private static readonly Regex TitleRegex = new(@"^#\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex SectionRegex = new(@"^##\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex RequirementRegex = new(@"^\s*-\s*(bins|envs|configs|os|toolGroups):\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ParameterLineRegex = new(
        @"^\s*-\s*(?<name>\w[\w\-]*)\s*:\s*(?<desc>[^(]+?)(?:\s*\((?<opts>[^)]*)\))?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex SlugInvalidCharsRegex = new(@"[^a-z0-9\-]", RegexOptions.Compiled);
    private static readonly Regex SlugCollapseHyphensRegex = new(@"-{2,}", RegexOptions.Compiled);

    public FileSystemSkillStore(
        ILogger<FileSystemSkillStore> logger,
        IOptions<AIOptions> options,
        IConfiguration? configuration = null,
        IToolRegistry? toolRegistry = null)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value.ContextProviders.Skills;
        _configuration = configuration;
        _toolRegistry = toolRegistry;
    }

    /// <inheritdoc/>
    public async Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        // 快速路径：缓存命中
        if (_cache != null && DateTime.UtcNow < _cacheExpiresAt)
        {
            return _cache;
        }

        await _cacheLock.WaitAsync(ct);
        try
        {
            // 双重检查
            if (_cache != null && DateTime.UtcNow < _cacheExpiresAt)
            {
                return _cache;
            }

            var skills = await LoadAllSkillsAsync(ct);
            _cache = skills;
            _cacheExpiresAt = DateTime.UtcNow + _options.CacheTtl;
            return skills;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 使缓存失效，下次调用 GetAllAsync 时重新加载
    /// </summary>
    public void InvalidateCache()
    {
        _cacheLock.Wait();
        try
        {
            _cache = null;
            _cacheExpiresAt = DateTime.MinValue;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    // -------------------------------------------------------------------------
    // 加载逻辑
    // -------------------------------------------------------------------------

    private async Task<List<SkillDefinition>> LoadAllSkillsAsync(CancellationToken ct)
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
                var loaded = await LoadSkillsFromPathAsync(path, ct);
                skills.AddRange(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skills from path: {Path}", path);
            }
        }

        skills = FilterSkills(skills);

        _logger.LogInformation("Loaded {Count} skills from {PathCount} paths",
            skills.Count, _options.Paths.Count);

        return skills;
    }

    private async Task<List<SkillDefinition>> LoadSkillsFromPathAsync(string path, CancellationToken ct)
    {
        var skills = new List<SkillDefinition>();

        if (!Directory.Exists(path))
        {
            _logger.LogDebug("Skill path does not exist: {Path}", path);
            return skills;
        }

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

    private async Task<SkillDefinition?> LoadSkillFromFileAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(filePath, ct);
        return ParseSkillContent(content, filePath);
    }

    // -------------------------------------------------------------------------
    // 解析逻辑
    // -------------------------------------------------------------------------

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
            Scope = SkillScope.System,
            Source = SkillSource.FileSystem
        };

        // 解析标题
        var titleMatch = TitleRegex.Match(content);
        if (titleMatch.Success)
        {
            skill.Name = titleMatch.Groups[1].Value.Trim();
        }
        else
        {
            skill.Name = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";
        }

        // 生成 slug（从技能名称）
        skill.Slug = GenerateSlug(skill.Name);

        // 解析各章节
        var sections = ParseSections(content);

        // 描述（标题后到第一个 ## 之间）
        skill.Description = ExtractDescription(content, titleMatch);

        // 使用场景
        if (sections.TryGetValue("when to use", out var whenToUse))
        {
            skill.WhenToUse = whenToUse.Trim();
        }

        // 参数
        if (sections.TryGetValue("parameters", out var parametersSection))
        {
            skill.Parameters = ParseParameters(parametersSection);
        }

        // 依赖要求
        if (sections.TryGetValue("requirements", out var requirements))
        {
            skill.Requirements = ParseRequirements(requirements);
        }

        // 元数据
        if (sections.TryGetValue("metadata", out var metadata))
        {
            skill.Metadata = ParseMetadata(metadata);
        }

        // 从元数据提取标准字段
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
        if (skill.Metadata.TryGetValue("enabled", out var enabledStr)
            && bool.TryParse(enabledStr, out var enabledValue))
        {
            skill.Enabled = enabledValue;
        }

        // 上下文修改器
        if (skill.Metadata.TryGetValue("allowed-tools", out var allowedTools))
        {
            skill.AllowedToolGroups = allowedTools
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        if (skill.Metadata.TryGetValue("model", out var model))
        {
            skill.RequiredModel = model;
        }
        if (skill.Metadata.TryGetValue("provider", out var provider))
        {
            skill.RequiredProvider = provider;
        }

        return skill;
    }

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
    /// 解析 ## Parameters 章节。每行格式：
    /// <c>- name: description (required|optional, allowed: v1, v2, default: v1)</c>
    /// </summary>
    private static List<SkillParameter> ParseParameters(string section)
    {
        var parameters = new List<SkillParameter>();
        var lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var match = ParameterLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var param = new SkillParameter
            {
                Name = match.Groups["name"].Value.Trim(),
                Description = match.Groups["desc"].Value.Trim()
            };

            var opts = match.Groups["opts"].Value;
            if (!string.IsNullOrWhiteSpace(opts))
            {
                // 是否必填
                param.Required = opts.Contains("required", StringComparison.OrdinalIgnoreCase)
                    && !opts.Contains("optional", StringComparison.OrdinalIgnoreCase);

                // 允许的枚举值：allowed: v1, v2
                var allowedMatch = Regex.Match(opts, @"allowed:\s*([^,)]+(?:,\s*[^,)]+)*?)(?:\s*,\s*default:|$|\))",
                    RegexOptions.IgnoreCase);
                if (allowedMatch.Success)
                {
                    param.AllowedValues = allowedMatch.Groups[1].Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                }

                // 默认值：default: v1
                var defaultMatch = Regex.Match(opts, @"default:\s*(.+?)(?:\s*,|\s*$|\))",
                    RegexOptions.IgnoreCase);
                if (defaultMatch.Success)
                {
                    param.DefaultValue = defaultMatch.Groups[1].Value.Trim();
                }
            }

            parameters.Add(param);
        }

        return parameters;
    }

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
                case "bins": requirements.Bins = values; break;
                case "envs": requirements.Envs = values; break;
                case "configs": requirements.Configs = values; break;
                case "os": requirements.Os = values; break;
                case "toolgroups": requirements.ToolGroups = values; break;
            }
        }

        return requirements;
    }

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

    // -------------------------------------------------------------------------
    // Slug 生成
    // -------------------------------------------------------------------------

    /// <summary>
    /// 从技能名称生成 kebab-case slug。
    /// 规则：小写化 → 空格/下划线转连字符 → 去除非 [a-z0-9-] → 合并连续连字符 → 去除首尾连字符 → 最长 64 字符。
    /// </summary>
    public static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unknown";
        }

        var slug = name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        slug = SlugInvalidCharsRegex.Replace(slug, string.Empty);
        slug = SlugCollapseHyphensRegex.Replace(slug, "-");
        slug = slug.Trim('-');

        if (slug.Length > 64)
        {
            slug = slug.Substring(0, 64).TrimEnd('-');
        }

        return string.IsNullOrEmpty(slug) ? "unknown" : slug;
    }

    // -------------------------------------------------------------------------
    // 过滤逻辑
    // -------------------------------------------------------------------------

    private List<SkillDefinition> FilterSkills(List<SkillDefinition> skills)
    {
        if (_options.AllowList.Count > 0)
        {
            skills = skills.Where(s =>
                _options.AllowList.Contains(s.Name, StringComparer.OrdinalIgnoreCase) ||
                _options.AllowList.Contains(s.Slug, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (_options.DenyList.Count > 0)
        {
            skills = skills.Where(s =>
                !_options.DenyList.Contains(s.Name, StringComparer.OrdinalIgnoreCase) &&
                !_options.DenyList.Contains(s.Slug, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        return skills;
    }

    // -------------------------------------------------------------------------
    // 依赖验证
    // -------------------------------------------------------------------------

    /// <summary>
    /// 验证技能依赖要求（bins/envs/configs/os/toolGroups）
    /// </summary>
    public virtual SkillValidationResult ValidateRequirements(SkillDefinition skill)
    {
        if (!_options.RequireChecksEnabled || skill.Requirements == null)
        {
            return new SkillValidationResult { IsValid = true };
        }

        var result = new SkillValidationResult { IsValid = true };

        foreach (var bin in skill.Requirements.Bins)
        {
            if (!IsBinaryAvailable(bin))
            {
                result.IsValid = false;
                result.MissingBins.Add(bin);
            }
        }

        foreach (var env in skill.Requirements.Envs)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
            {
                result.IsValid = false;
                result.MissingEnvs.Add(env);
            }
        }

        if (_configuration != null)
        {
            foreach (var config in skill.Requirements.Configs)
            {
                if (string.IsNullOrEmpty(_configuration[config]))
                {
                    result.IsValid = false;
                    result.MissingConfigs.Add(config);
                }
            }
        }

        if (_toolRegistry != null)
        {
            foreach (var group in skill.Requirements.ToolGroups)
            {
                var tools = _toolRegistry.GetToolsByGroup(group);
                if (tools.Count == 0)
                {
                    result.IsValid = false;
                    result.MissingToolGroups.Add(group);
                }
            }
        }

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

    private static string GetCurrentOs()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "unknown";
    }
}
