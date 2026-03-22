namespace Tnzi.AI.Options;

/// <summary>
/// AI Utility 全局默认配置，绑定 AI:Utility 配置节
/// </summary>
public class AiUtilityOptions
{
    /// <summary>
    /// 默认模型名称（null = 使用 Provider 默认模型）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 默认最大输出 Token 数
    /// </summary>
    public int MaxTokens { get; set; } = 100;

    /// <summary>
    /// 默认温度参数
    /// </summary>
    public double Temperature { get; set; } = 0.3;
}

/// <summary>
/// IAiUtility.ExecuteAsync 的单次调用覆盖选项
/// </summary>
public class AiUtilityCallOptions
{
    /// <summary>
    /// 覆盖模型
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 覆盖最大输出 Token 数
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// 覆盖温度参数
    /// </summary>
    public double? Temperature { get; init; }
}
