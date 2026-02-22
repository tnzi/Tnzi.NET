namespace Tnzi.AI.Services;

/// <summary>
/// 空操作文本搜索服务 - 不执行实际搜索，返回空结果
/// </summary>
/// <remarks>
/// <para>
/// 这是 ITextSearchService 的默认实现，用于在用户未配置向量存储时提供回退。
/// 当用户需要启用 RAG 功能时，应注册自己的 ITextSearchService 实现。
/// </para>
/// </remarks>
public class NoOpTextSearchService : ITextSearchService
{
    private readonly ILogger<NoOpTextSearchService> _logger;

    public NoOpTextSearchService(ILogger<NoOpTextSearchService> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "NoOpTextSearchService.SearchAsync called with query length {Length}. " +
            "Register a custom ITextSearchService implementation to enable RAG.",
            query?.Length ?? 0);

        return Task.FromResult<IEnumerable<TextSearchResult>>([]);
    }
}
