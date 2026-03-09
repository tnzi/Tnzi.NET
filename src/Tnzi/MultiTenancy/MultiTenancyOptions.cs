namespace Tnzi.MultiTenancy;

/// <summary>
/// 多租户开关配置
/// 配置节：MultiTenancy
/// </summary>
public class MultiTenancyOptions
{
    /// <summary>
    /// 是否启用多租户能力（默认关闭）
    /// </summary>
    public bool Enabled { get; set; } = false;
}
