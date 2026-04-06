namespace Tnzi.AI.Memory;

/// <summary>
/// IMemorySideQuery 默认实现 — 通过 IAiUtility 轻量 LLM 调用预筛记忆相关性
/// </summary>
public class MemorySideQuery : IMemorySideQuery
{
    private const int MaxContentPreviewLength = 100;
    private const int MaxTokens = 256;

    private static readonly string SystemPrompt = """
        You are a relevance filter. Given a list of numbered memory entries and a user query,
        return ONLY a JSON array of the entry indices (integers) that are relevant to the query.
        Order by relevance (most relevant first). If none are relevant, return an empty array [].
        Respond with ONLY the JSON array, no explanation.
        """;

    private readonly IAiUtility _aiUtility;
    private readonly ILogger<MemorySideQuery> _logger;

    public MemorySideQuery(IAiUtility aiUtility, ILogger<MemorySideQuery>? logger = null)
    {
        _aiUtility = Check.NotNull(aiUtility);
        _logger = logger ?? NullLogger<MemorySideQuery>.Instance;
    }

    public async Task<IReadOnlyList<int>> FilterRelevantAsync(
        IReadOnlyList<MemorySnippet> entries, string query, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(query);

        if (entries is null || entries.Count == 0)
            return [];

        var userMessage = BuildUserMessage(entries, query);

        try
        {
            var response = await _aiUtility.ExecuteAsync(
                SystemPrompt,
                userMessage,
                new AiUtilityCallOptions { MaxTokens = MaxTokens },
                ct);

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogDebug("Memory side query returned empty response, falling back to all indices");
                return FallbackAllIndices(entries);
            }

            return ParseIndices(response, entries.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory side query failed, falling back to all indices");
            return FallbackAllIndices(entries);
        }
    }

    private static string BuildUserMessage(IReadOnlyList<MemorySnippet> entries, string query)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Memory entries:");

        foreach (var entry in entries)
        {
            var preview = entry.ContentPreview.Length > MaxContentPreviewLength
                ? entry.ContentPreview[..MaxContentPreviewLength] + "..."
                : entry.ContentPreview;

            var categoryPart = string.IsNullOrEmpty(entry.Category) ? "" : $" [{entry.Category}]";
            sb.AppendLine($"  {entry.Index}.{categoryPart} {preview}");
        }

        sb.AppendLine();
        sb.AppendLine($"Query: {query}");

        return sb.ToString();
    }

    /// <summary>
    /// 解析 LLM 返回的 JSON 索引数组，过滤无效索引
    /// </summary>
    private IReadOnlyList<int> ParseIndices(string response, int entryCount)
    {
        // 提取 JSON 数组部分（LLM 可能返回额外文本）
        var startIndex = response.IndexOf('[');
        var endIndex = response.LastIndexOf(']');

        if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
            return Enumerable.Range(0, entryCount).ToList();

        var jsonPart = response[startIndex..(endIndex + 1)];

        try
        {
            var indices = JsonSerializer.Deserialize<int[]>(jsonPart);

            if (indices is null || indices.Length == 0)
                return [];

            // 过滤无效索引，保持 LLM 返回的相关性排序
            return indices
                .Where(i => i >= 0 && i < entryCount)
                .Distinct()
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse memory side query response as JSON: {Response}",
                jsonPart.Length > 200 ? jsonPart[..200] : jsonPart);
            return Enumerable.Range(0, entryCount).ToList();
        }
    }

    private static IReadOnlyList<int> FallbackAllIndices(IReadOnlyList<MemorySnippet> entries)
    {
        return Enumerable.Range(0, entries.Count).ToList();
    }
}
