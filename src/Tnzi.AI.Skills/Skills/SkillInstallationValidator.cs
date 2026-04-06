using System.IO.Compression;

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
        "paths"
    };

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugValidationRegex();

    // -------------------------------------------------------------------------
    // ZIP Content Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// 验证 ZIP 包内容安全性（ZIP Bomb、符号链接、路径遍历）
    /// </summary>
    public static InstallationValidationResult ValidateZipContent(ZipArchive archive)
    {
        Check.NotNull(archive);

        long totalUncompressedSize = 0;
        long totalCompressedSize = 0;

        foreach (var entry in archive.Entries)
        {
            // 1. 符号链接检测（Unix external_attr）
            if (IsSymlink(entry))
            {
                return InstallationValidationResult.Failure($"ZIP entry '{entry.FullName}' appears to be a symlink");
            }

            // 2. 路径遍历检测
            var pathValidation = ValidatePathSafety(entry.FullName);
            if (!pathValidation.IsValid)
            {
                return pathValidation;
            }

            // 3. 累计大小
            totalUncompressedSize += entry.Length;
            totalCompressedSize += entry.CompressedLength;
        }

        // 4. ZIP Bomb 检测 — 总解压大小
        if (totalUncompressedSize > MaxUncompressedSize)
        {
            return InstallationValidationResult.Failure(
                $"ZIP uncompressed size ({totalUncompressedSize:N0} bytes) exceeds limit ({MaxUncompressedSize:N0} bytes)");
        }

        // 5. ZIP Bomb 检测 — 压缩比
        if (totalCompressedSize > 0)
        {
            var ratio = (double)totalUncompressedSize / totalCompressedSize;
            if (ratio > MaxCompressionRatio)
            {
                return InstallationValidationResult.Failure(
                    $"ZIP compression ratio ({ratio:F1}) exceeds limit ({MaxCompressionRatio})");
            }
        }

        return InstallationValidationResult.Success();
    }

    // -------------------------------------------------------------------------
    // Path Safety Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// 验证提取路径安全性（拒绝绝对路径、.. 遍历、反斜杠分隔符）
    /// </summary>
    public static InstallationValidationResult ValidatePathSafety(string path)
    {
        Check.NotNullOrWhiteSpace(path);

        // 拒绝绝对路径
        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\'))
        {
            return InstallationValidationResult.Failure($"Path '{path}' is absolute, only relative paths are allowed");
        }

        // 拒绝路径遍历
        var segments = path.Split('/', '\\');
        if (segments.Any(s => s == ".."))
        {
            return InstallationValidationResult.Failure($"Path '{path}' contains directory traversal (..)");
        }

        // 拒绝反斜杠分隔符（跨平台安全）
        if (path.Contains('\\'))
        {
            return InstallationValidationResult.Failure($"Path '{path}' contains backslash separators");
        }

        return InstallationValidationResult.Success();
    }

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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// 检测 ZIP 条目是否为符号链接（通过 Unix external attributes）
    /// </summary>
    private static bool IsSymlink(ZipArchiveEntry entry)
    {
        // Unix 符号链接: external_attr 的高 16 位包含 Unix 模式，0xA000 = symlink
        var externalAttrs = entry.ExternalAttributes;
        var unixMode = (externalAttrs >> 16) & 0xFFFF;
        return (unixMode & 0xA000) == 0xA000;
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
