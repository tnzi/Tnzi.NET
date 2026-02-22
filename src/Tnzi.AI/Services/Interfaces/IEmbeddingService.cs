namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 嵌入服务接口
/// </summary>
/// <remarks>
/// 定义生成文本嵌入向量的标准方法，供 RAG/向量检索等能力使用。
/// 实现基于 OpenAI 兼容的 /embeddings 接口，支持配置中任意提供商的 BaseUrl + ApiKey。
/// </remarks>
public interface IEmbeddingService
{
    /// <summary>
    /// 生成文本的嵌入向量
    /// </summary>
    /// <param name="text">输入文本</param>
    /// <param name="options">嵌入选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含嵌入向量的结果</returns>
    Task<Result<float[]>> GenerateEmbeddingAsync(
        string text,
        EmbeddingOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 批量生成文本的嵌入向量
    /// </summary>
    /// <param name="texts">输入文本列表</param>
    /// <param name="options">嵌入选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含嵌入向量列表的结果</returns>
    Task<Result<List<float[]>>> GenerateEmbeddingsAsync(
        List<string> texts,
        EmbeddingOptions? options = null,
        CancellationToken ct = default);
}
