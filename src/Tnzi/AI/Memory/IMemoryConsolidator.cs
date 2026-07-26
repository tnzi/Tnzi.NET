namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆合并器 - 对比新记忆与已有记忆，决定 ADD/UPDATE/DELETE/NOOP
/// </summary>
/// <remarks>
/// 默认实现为 LlmMemoryConsolidator（使用 LLM 做语义比对），
/// 也可实现基于规则的合并器。
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IMemoryConsolidator
{
    /// <summary>
    /// 合并判断 - 对比新记忆与已有相似记忆
    /// </summary>
    /// <param name="newMemory">新提取的记忆内容</param>
    /// <param name="existingMemories">已有的相似记忆（通过 SearchAsync 检索）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>合并决策结果</returns>
    Task<MemoryConsolidationResult> ConsolidateAsync(
        string newMemory,
        IReadOnlyList<MemorySearchResult> existingMemories,
        CancellationToken ct = default);
}

/// <summary>
/// 记忆合并决策结果
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public record MemoryConsolidationResult(
    MemoryAction Action,
    string? UpdatedContent = null,
    Guid? TargetEntryId = null);

/// <summary>
/// 记忆合并动作
/// </summary>
public enum MemoryAction
{
    /// <summary>新增 - 新记忆包含新信息</summary>
    Add,
    /// <summary>更新 - 新记忆更新/替代已有记忆</summary>
    Update,
    /// <summary>删除 - 新记忆与已有记忆矛盾，应删除旧记忆</summary>
    Delete,
    /// <summary>无操作 - 新记忆与已有记忆重复</summary>
    Noop
}
