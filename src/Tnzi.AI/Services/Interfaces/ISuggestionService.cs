namespace Tnzi.AI.Services;

/// <summary>
/// 后续建议生成服务接口
/// </summary>
public interface ISuggestionService
{
    /// <summary>
    /// 根据对话上下文生成后续建议问题
    /// </summary>
    Task<List<string>> GenerateAsync(Guid threadId, int count = 3, CancellationToken ct = default);
}
