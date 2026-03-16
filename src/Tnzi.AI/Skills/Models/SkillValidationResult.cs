namespace Tnzi.AI.Skills;

/// <summary>
/// 技能依赖验证结果
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
    /// 缺失的配置项
    /// </summary>
    public List<string> MissingConfigs { get; set; } = [];

    /// <summary>
    /// 缺失的工具组
    /// </summary>
    public List<string> MissingToolGroups { get; set; } = [];

    /// <summary>
    /// 获取验证失败的原因描述
    /// </summary>
    public string GetFailureReason()
    {
        var reasons = new List<string>();

        if (MissingBins.Count > 0)
            reasons.Add($"Missing binaries: {string.Join(", ", MissingBins)}");
        if (MissingEnvs.Count > 0)
            reasons.Add($"Missing environment variables: {string.Join(", ", MissingEnvs)}");
        if (MissingConfigs.Count > 0)
            reasons.Add($"Missing configs: {string.Join(", ", MissingConfigs)}");
        if (MissingToolGroups.Count > 0)
            reasons.Add($"Missing tool groups: {string.Join(", ", MissingToolGroups)}");
        if (!string.IsNullOrEmpty(UnsupportedOs))
            reasons.Add($"Unsupported OS: {UnsupportedOs}");

        return string.Join("; ", reasons);
    }
}
