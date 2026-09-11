namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 基于 LLM 的知识图谱实体关系提取器 - 使用 IAiUtility 调用 LLM 从文本中提取实体和关系
/// </summary>
public class LlmGraphExtractor : IGraphExtractor
{
    private readonly IAiUtility _aiUtility;
    private readonly IRepository<KnowledgeGraphNode, Guid> _nodeRepository;
    private readonly IRepository<KnowledgeGraphEdge, Guid> _edgeRepository;
    private readonly ILogger<LlmGraphExtractor> _logger;

    /// <summary>
    /// Maximum character count for text sent to LLM (prevents exceeding context window).
    /// Texts longer than this will be truncated with a warning.
    /// </summary>
    private const int MaxTextLength = 30_000;

    /// <summary>
    /// 图谱抽取一次调用的输出上限。抽取产出的是一整份 JSON，被截断就整份解析失败，
    /// 所以绝不能用 <c>IAiUtility</c> 那个为极短输出定的全局默认。
    /// </summary>
    private const int ExtractionMaxTokens = 4096;

    private const string SystemPrompt = """
        You are a knowledge graph extraction engine. Given a text, extract entities and relationships.

        Output ONLY a JSON object with this exact schema (no markdown, no explanation):
        {
          "entities": [
            {
              "name": "entity name",
              "type": "Person|Organization|Concept|Location|Event|Technology|Other",
              "description": "brief description"
            }
          ],
          "relationships": [
            {
              "source": "source entity name",
              "target": "target entity name",
              "relation": "relationship type (e.g. works_for, located_in, related_to, created_by)",
              "description": "brief description of the relationship",
              "weight": 0.8
            }
          ]
        }

        Rules:
        - Extract all meaningful entities and their relationships
        - Entity names must be exact as they appear in the text
        - Relationship source/target must match entity names exactly
        - Weight is a confidence score from 0.0 to 1.0
        - If no entities found, return {"entities": [], "relationships": []}
        """;

    public LlmGraphExtractor(
        IAiUtility aiUtility,
        IRepository<KnowledgeGraphNode, Guid> nodeRepository,
        IRepository<KnowledgeGraphEdge, Guid> edgeRepository,
        ILogger<LlmGraphExtractor> logger)
    {
        _aiUtility = Check.NotNull(aiUtility);
        _nodeRepository = Check.NotNull(nodeRepository);
        _edgeRepository = Check.NotNull(edgeRepository);
        _logger = Check.NotNull(logger);
    }

    public async Task<GraphExtractionResult> ExtractAsync(string text, Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("Empty text provided for graph extraction, returning empty result");
            return GraphExtractionResult.Empty;
        }

        // 超大文本保护：截断并 log warning
        var inputText = text;
        if (inputText.Length > MaxTextLength)
        {
            _logger.LogWarning(
                "Graph extraction text exceeds max length ({TextLength} > {MaxLength}), truncating",
                inputText.Length, MaxTextLength);
            inputText = inputText[..MaxTextLength];
        }

        var response = await _aiUtility.ExecuteAsync(
            SystemPrompt,
            inputText,
            // 显式传上限，不依赖 IAiUtility 的全局默认（那个默认是给标题生成这类极短输出定的，
            // 用它抽图谱会把 JSON 截断在半路 —— 解析失败，而失败的样子和"没有实体"一模一样）。
            new AiUtilityCallOptions { MaxTokens = ExtractionMaxTokens },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("LLM returned empty response for graph extraction");
            return GraphExtractionResult.Empty;
        }

        var (nodes, edges, parsed) = ParseResponse(response, knowledgeBaseId);

        if (!parsed)
        {
            // ★ 解析失败绝不能和"确实没有实体"给出同一个结果：两者都是空集合，
            // 但前者意味着这段文本的图谱从未被抽出来过，而调用方读到的是"抽完了，没东西"。
            // 截断是最常见的成因，所以把长度与结尾一起报出来 —— 一个不以 '}' 收尾的
            // 响应几乎一定是被 MaxTokens 砍断的。
            var trimmed = response.TrimEnd();
            _logger.LogWarning(
                "Graph extraction response could not be parsed as JSON for knowledge base {KnowledgeBaseId}: " +
                "{Length} chars, ends with '{Tail}' (looksTruncated={LooksTruncated}). " +
                "No graph was extracted for this text.",
                knowledgeBaseId,
                response.Length,
                trimmed.Length <= 40 ? trimmed : trimmed[^40..],
                !trimmed.EndsWith('}'));

            return GraphExtractionResult.Unparsable;
        }

        if (nodes.Count == 0)
        {
            _logger.LogDebug("No entities extracted from text");
            return GraphExtractionResult.Empty;
        }

        // 持久化节点
        await _nodeRepository.InsertManyAsync(nodes, cancellationToken);

        // 主键由框架在 SaveChanges 时生成：存在环境事务（UnitOfWork）时 InsertMany 是延迟提交，
        // 此刻 Id 仍为 Guid.Empty，边的外键会全部指向空 GUID（提交时外键违反）。显式 flush 让 Id 落定。
        if (nodes.Exists(n => n.Id == Guid.Empty))
        {
            await _nodeRepository.SaveChangesAsync(cancellationToken);
        }

        // 建立 name → node ID 映射，用于解析边的外键。
        // LLM 完全可能对同一实体重复输出（含仅大小写不同），故用 TryAdd 而非 ToDictionary
        // （后者遇重复键直接抛 ArgumentException，节点已入库、整次抽取报废）。首次出现者胜出。
        var nodeIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            nodeIdMap.TryAdd(node.Name, node.Id);
        }

        // 将原始边的 source/target name 映射为实际 node ID，过滤无效引用
        var validEdges = ResolveEdgeNodeIds(edges, nodeIdMap, knowledgeBaseId);

        if (validEdges.Count > 0)
        {
            await _edgeRepository.InsertManyAsync(validEdges, cancellationToken);
        }

        _logger.LogInformation("Graph extraction completed: {NodeCount} nodes, {EdgeCount} edges for knowledge base {KnowledgeBaseId}",
            nodes.Count, validEdges.Count, knowledgeBaseId);

        return new GraphExtractionResult(nodes, validEdges);
    }

    /// <summary>
    /// 解析 LLM JSON 响应为节点和边（边暂不设置 SourceNodeId/TargetNodeId，后续通过 name 映射）
    /// </summary>
    /// <returns>
    /// 节点、边，以及<b>是否解析成功</b>。第三项不能省：解析失败与"文本里确实没有实体"
    /// 都产出空集合，少了它两者在调用方眼里完全一样。
    /// </returns>
    internal (List<KnowledgeGraphNode> Nodes, List<RawEdge> Edges, bool Parsed) ParseResponse(string json, Guid knowledgeBaseId)
    {
        var nodes = new List<KnowledgeGraphNode>();
        var edges = new List<RawEdge>();

        try
        {
            // Strip markdown code fence if LLM wraps response in ```json...```
            var cleanJson = StripMarkdownFence(json);
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            // 解析实体
            if (root.TryGetProperty("entities", out var entitiesElement))
            {
                foreach (var entity in entitiesElement.EnumerateArray())
                {
                    // 用 TryGetProperty 而非 GetProperty：字段整个缺失时 GetProperty 抛的是
                    // KeyNotFoundException，不被下方 catch (JsonException) 捕获，一条畸形实体会让整次抽取失败。
                    var name = ReadString(entity, "name");
                    var type = ReadString(entity, "type");

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                        continue;

                    var node = new KnowledgeGraphNode
                    {
                        KnowledgeBaseId = knowledgeBaseId,
                        Name = name,
                        EntityType = type,
                        Description = ReadString(entity, "description")
                    };

                    nodes.Add(node);
                }
            }

            // 解析关系
            if (root.TryGetProperty("relationships", out var relsElement))
            {
                foreach (var rel in relsElement.EnumerateArray())
                {
                    var source = ReadString(rel, "source");
                    var target = ReadString(rel, "target");
                    var relation = ReadString(rel, "relation");

                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(relation))
                        continue;

                    double? weight = rel.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number
                        ? w.GetDouble()
                        : null;

                    edges.Add(new RawEdge(source, target, relation, ReadString(rel, "description"), weight));
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM graph extraction response as JSON");
            return (nodes, edges, false);
        }

        return (nodes, edges, true);
    }

    /// <summary>
    /// 读取字符串字段：字段缺失或不是字符串时返回 null（LLM 输出不可信，不能假定字段存在）
    /// </summary>
    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// 将原始边的 source/target name 映射为实际 node ID
    /// </summary>
    private static List<KnowledgeGraphEdge> ResolveEdgeNodeIds(List<RawEdge> rawEdges, Dictionary<string, Guid> nodeIdMap, Guid knowledgeBaseId)
    {
        var resolved = new List<KnowledgeGraphEdge>();

        foreach (var raw in rawEdges)
        {
            if (!nodeIdMap.TryGetValue(raw.Source, out var sourceId) ||
                !nodeIdMap.TryGetValue(raw.Target, out var targetId))
            {
                continue; // 跳过引用不存在节点的边
            }

            resolved.Add(new KnowledgeGraphEdge
            {
                KnowledgeBaseId = knowledgeBaseId,
                SourceNodeId = sourceId,
                TargetNodeId = targetId,
                RelationType = raw.Relation,
                Description = raw.Description,
                Weight = raw.Weight
            });
        }

        return resolved;
    }

    /// <summary>
    /// Strip markdown code fence wrapper (e.g. ```json ... ```) from LLM response
    /// </summary>
    internal static string StripMarkdownFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            // Remove opening fence line (```json, ```JSON, ```, etc.)
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0) return trimmed;
            trimmed = trimmed[(firstNewline + 1)..];

            // Remove closing fence
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                trimmed = trimmed[..lastFence];
            }

            return trimmed.Trim();
        }

        return text;
    }

    /// <summary>
    /// 原始边数据（LLM 输出使用 name 引用而非 ID）
    /// </summary>
    internal sealed record RawEdge(string Source, string Target, string Relation, string? Description, double? Weight);
}
