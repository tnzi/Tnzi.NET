namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 结构化输出服务接口
/// </summary>
/// <remarks>
/// <para>
/// 提供将 AI 响应解析为结构化数据的功能。
/// 支持 JSON Schema 验证和类型安全的反序列化。
/// </para>
/// </remarks>
public interface IStructuredOutputService
{
    /// <summary>
    /// 获取结构化输出
    /// </summary>
    /// <typeparam name="T">输出类型</typeparam>
    /// <param name="prompt">提示词</param>
    /// <param name="options">可选的输出选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>结构化输出结果</returns>
    Task<Result<T>> GetStructuredOutputAsync<T>(
        string prompt,
        StructuredOutputOptions? options = null,
        CancellationToken ct = default) where T : class;

    /// <summary>
    /// 获取结构化输出（带上下文消息）
    /// </summary>
    /// <typeparam name="T">输出类型</typeparam>
    /// <param name="messages">对话消息列表</param>
    /// <param name="options">可选的输出选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>结构化输出结果</returns>
    Task<Result<T>> GetStructuredOutputAsync<T>(
        IEnumerable<ChatMessage> messages,
        StructuredOutputOptions? options = null,
        CancellationToken ct = default) where T : class;
}

/// <summary>
/// 结构化输出选项
/// </summary>
public class StructuredOutputOptions
{
    /// <summary>
    /// 提供商名称
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 模型 ID
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// 是否启用严格模式（强制 AI 输出符合 Schema）
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// 最大重试次数（解析失败时）
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 温度参数
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// 最大输出 Token 数（UseAgent 时通过 ChatOptions 传递给 Agent）
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// 系统提示词（可选，用于指导输出格式）
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// 自定义 JSON Schema（可选，默认从类型生成）
    /// </summary>
    public string? JsonSchema { get; set; }

    /// <summary>
    /// 是否通过 ChatClientAgent 执行（可复用 ContextProviders/Tools）
    /// </summary>
    /// <remarks>
    /// 默认 false（轻量模式，直接用 IChatClient）；设为 true 时通过 AgentFactory 创建 Agent 并调用 RunAsync&lt;T&gt;。
    /// </remarks>
    public bool UseAgent { get; set; } = false;
}
