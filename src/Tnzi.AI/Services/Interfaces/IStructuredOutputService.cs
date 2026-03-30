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
