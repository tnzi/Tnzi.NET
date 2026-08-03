namespace Tnzi.AI.Services;

/// <summary>
/// 轻量级系统级 AI 调用工具 - 不加载 tools/skills/middleware，
/// 用于框架内部任务（标题生成、摘要、分类等）。
/// </summary>
public interface IAiUtility
{
    /// <summary>
    /// 执行一次简单的 AI 请求
    /// </summary>
    /// <param name="systemPrompt">系统提示词</param>
    /// <param name="userMessage">用户消息</param>
    /// <param name="options">单次调用覆盖选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>AI 回复文本，失败返回 null</returns>
    Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default);
}
