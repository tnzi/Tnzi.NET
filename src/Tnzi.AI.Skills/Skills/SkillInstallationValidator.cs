namespace Tnzi.AI.Skills;

/// <summary>
/// Skill 安装安全验证器 — 验证 ZIP 包、frontmatter、名称、描述和路径安全性。
/// </summary>
/// <remarks>
/// 防御向量包括：ZIP Bomb、符号链接、路径遍历、YAML 注入、名称注入。
/// 所有验证均为静态方法，可独立使用。
/// </remarks>
public static partial class SkillInstallationValidator
{
    /// <summary>最大解压大小 (512 MB)</summary>
    private const long MaxUncompressedSize = 512 * 1024 * 1024;

    /// <summary>最大压缩比（解压大小/压缩大小）</summary>
    private const double MaxCompressionRatio = 100;

    /// <summary>Slug 最大长度</summary>
    private const int MaxSlugLength = 64;

    /// <summary>描述最大长度</summary>
    private const int MaxDescriptionLength = 1024;

    /// <summary>允许的 frontmatter 键（小写）</summary>
    private static readonly HashSet<string> AllowedFrontmatterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "slug", "description", "version", "author", "tags",
        "priority", "enabled", "agents", "allowed-tool-groups",
        "tool-whitelist", "tool-blacklist", "allowed-tools", "denied-tools",
        "model", "provider", "reasoning-effort", "execution-context",
        "internal", "license", "homepage", "keywords", "category",
        "paths",
        // Routing-signal hint (Phase 3.2). All three casing styles are
        // accepted so frontmatter authors can pick whichever matches the
        // rest of their YAML conventions.
        "when-to-use", "whenToUse", "when_to_use"
    };

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugValidationRegex();

    // -------------------------------------------------------------------------
    // Frontmatter Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// 验证 frontmatter 键安全性（YAML 注入防御 — 拒绝未知键）
    /// </summary>
    public static InstallationValidationResult ValidateFrontmatter(Dictionary<string, string> frontmatter)
    {
        Check.NotNull(frontmatter);

        foreach (var key in frontmatter.Keys)
        {
            if (!AllowedFrontmatterKeys.Contains(key))
            {
                return InstallationValidationResult.Failure($"Unknown frontmatter key '{key}' is not allowed");
            }
        }

        return InstallationValidationResult.Success();
    }

    // -------------------------------------------------------------------------
    // Name / Description Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// 验证 Skill slug 格式（kebab-case, 1-64 字符, 仅小写字母数字和连字符）
    /// </summary>
    public static InstallationValidationResult ValidateName(string name)
    {
        Check.NotNullOrWhiteSpace(name);

        if (name.Length > MaxSlugLength)
        {
            return InstallationValidationResult.Failure($"Skill name '{name}' exceeds maximum length of {MaxSlugLength} characters");
        }

        if (!SlugValidationRegex().IsMatch(name))
        {
            return InstallationValidationResult.Failure($"Skill name '{name}' must be kebab-case (lowercase alphanumeric with hyphens)");
        }

        return InstallationValidationResult.Success();
    }

    /// <summary>
    /// 验证 Skill 描述安全性（拒绝角括号 HTML/XML 注入, 长度限制）
    /// </summary>
    public static InstallationValidationResult ValidateDescription(string description)
    {
        Check.NotNullOrWhiteSpace(description);

        if (description.Length > MaxDescriptionLength)
        {
            return InstallationValidationResult.Failure($"Description exceeds maximum length of {MaxDescriptionLength} characters");
        }

        if (description.Contains('<') || description.Contains('>'))
        {
            return InstallationValidationResult.Failure("Description must not contain angle brackets (< >)");
        }

        return InstallationValidationResult.Success();
    }
}

/// <summary>
/// Skill 安装安全验证结果
/// </summary>
public record InstallationValidationResult(bool IsValid, string? ErrorMessage = null)
{
    /// <summary>创建成功结果</summary>
    public static InstallationValidationResult Success() => new(true);

    /// <summary>创建失败结果</summary>
    public static InstallationValidationResult Failure(string message) => new(false, message);
}
