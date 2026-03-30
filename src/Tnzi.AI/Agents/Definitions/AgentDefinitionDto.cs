namespace Tnzi.AI.Agents.Definitions;

/// <summary>
/// YAML Agent 定义 DTO — 映射 YAML 文件结构
/// </summary>
public class AgentDefinitionDto
{
    /// <summary>Agent 名称（必需）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Agent 描述</summary>
    public string? Description { get; set; }

    /// <summary>系统提示词</summary>
    public string? Instructions { get; set; }

    /// <summary>提供商名称</summary>
    public string? Provider { get; set; }

    /// <summary>模型名称</summary>
    public string? Model { get; set; }

    /// <summary>工具组列表</summary>
    public List<string>? ToolGroups { get; set; }

    /// <summary>温度参数</summary>
    public double? Temperature { get; set; }

    /// <summary>最大 Token 数</summary>
    public int? MaxTokens { get; set; }

    /// <summary>超时时间（秒）</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>执行模式（0=Single, 1=Handoff, 2=AgentAsTools, 3=Router, 4=ExternalCli）</summary>
    public int ExecutionMode { get; set; }

    /// <summary>额外配置（JSON 字符串）</summary>
    public string? Configuration { get; set; }

    /// <summary>领域标签</summary>
    public List<string>? Domains { get; set; }

    /// <summary>角色标签</summary>
    public List<string>? Roles { get; set; }

    /// <summary>质量等级 (1-5)</summary>
    public int QualityTier { get; set; } = 3;

    /// <summary>延迟等级 (1-5)</summary>
    public int LatencyTier { get; set; } = 3;

    /// <summary>成本等级 (1-5)</summary>
    public int CostTier { get; set; } = 3;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>定义内容的 SHA256 哈希值（用于变更检测）</summary>
    public string? DefinitionHash { get; set; }
}
