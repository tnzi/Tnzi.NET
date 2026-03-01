namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// LLM 实体抽取器 — 使用 LLM 从文本中提取命名实体
/// </summary>
[ExperimentalApi(Reason = "LLM entity extraction is in preview")]
public class LlmEntityExtractor
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptions<AIOptions> _options;
    private readonly ILogger<LlmEntityExtractor> _logger;

    private const string SystemPrompt =
        """
        Extract named entities from the text. Return a JSON array with this format:
        [{"name":"...","type":"person|organization|location|concept","properties":{}}]

        Rules:
        - "type" must be one of: person, organization, location, concept
        - "properties" is an optional object with string key-value pairs for notable attributes
        - Return ONLY the JSON array, nothing else
        - If no entities found, return an empty array: []
        """;

    public LlmEntityExtractor(
        IChatClientFactory chatClientFactory,
        IOptions<AIOptions> options,
        ILogger<LlmEntityExtractor> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 从文本中提取命名实体
    /// </summary>
    /// <param name="text">待提取文本</param>
    /// <param name="providerName">提供商名称（null 使用 EntityMemory 配置或默认）</param>
    /// <param name="modelId">模型 ID（null 使用 EntityMemory 配置或默认）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提取到的实体列表，失败时返回空列表</returns>
    public async Task<List<EntityMemoryEntry>> ExtractAsync(string text, string? providerName = null, string? modelId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            // 优先使用参数，其次使用 EntityMemory 配置，最后使用默认提供商
            var entityMemoryOptions = _options.Value.ContextProviders.EntityMemory;
            var provider = providerName ?? entityMemoryOptions.ExtractionProvider;
            var model = modelId ?? entityMemoryOptions.ExtractionModel;

            var chatClient = _chatClientFactory.GetChatClient(provider, model);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, text)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var responseText = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogDebug("LLM returned empty response for entity extraction");
                return [];
            }

            return ParseEntities(responseText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract entities from text (length: {Length})", text.Length);
            return [];
        }
    }

    /// <summary>
    /// 解析 LLM 返回的 JSON 实体列表
    /// </summary>
    private List<EntityMemoryEntry> ParseEntities(string json)
    {
        try
        {
            // 尝试提取 JSON 数组（LLM 可能包裹在 markdown 代码块中）
            var cleanJson = ExtractJsonArray(json);

            var entities = JsonSerializer.Deserialize<List<ExtractedEntity>>(cleanJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entities == null || entities.Count == 0)
            {
                return [];
            }

            var results = new List<EntityMemoryEntry>();
            foreach (var entity in entities)
            {
                if (string.IsNullOrWhiteSpace(entity.Name) || string.IsNullOrWhiteSpace(entity.Type))
                {
                    continue;
                }

                results.Add(new EntityMemoryEntry
                {
                    EntityName = entity.Name.Trim(),
                    EntityType = entity.Type.Trim().ToLower(),
                    Properties = entity.Properties ?? new Dictionary<string, string>(),
                    LastMentioned = DateTime.UtcNow,
                    MentionCount = 1
                });
            }

            _logger.LogDebug("Extracted {Count} entities from text", results.Count);
            return results;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse entity extraction JSON response: {Json}", json);
            return [];
        }
    }

    /// <summary>
    /// 从可能包含 markdown 代码块的文本中提取 JSON 数组
    /// </summary>
    private static string ExtractJsonArray(string text)
    {
        // 尝试提取 ```json ... ``` 代码块
        var match = Regex.Match(text, @"```(?:json)?\s*\n?(.*?)\n?\s*```", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // 尝试直接找到 JSON 数组
        var startIndex = text.IndexOf('[');
        var endIndex = text.LastIndexOf(']');
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return text[startIndex..(endIndex + 1)];
        }

        return text;
    }

    /// <summary>
    /// LLM 返回的实体结构（内部 DTO）
    /// </summary>
    private sealed class ExtractedEntity
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public Dictionary<string, string>? Properties { get; set; }
    }
}
