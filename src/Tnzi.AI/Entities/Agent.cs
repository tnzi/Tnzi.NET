namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 定义实体
/// </summary>
public class Agent : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Agent 名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// 提供商名称
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 工具组列表（JSON 数组）
    /// </summary>
    public string? ToolGroups { get; set; }

    /// <summary>
    /// 温度参数
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// 最大 Token 数
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 额外配置（JSON）
    /// </summary>
    public string? Configuration { get; set; }
}
