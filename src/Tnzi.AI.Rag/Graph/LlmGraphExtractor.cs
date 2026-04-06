namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 基于 LLM 的知识图谱实体关系提取器 — 使用 IAiUtility 调用 LLM 从文本中提取实体和关系
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
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("LLM returned empty response for graph extraction");
            return GraphExtractionResult.Empty;
        }

        var (nodes, edges) = ParseResponse(response, knowledgeBaseId);

        if (nodes.Count == 0)
        {
            _logger.LogDebug("No entities extracted from text");
            return GraphExtractionResult.Empty;
        }

        // 持久化节点
        await _nodeRepository.InsertManyAsync(nodes, cancellationToken);

        // 建立 name → node ID 映射，用于解析边的外键
        var nodeIdMap = nodes.ToDictionary(n => n.Name, n => n.Id, StringComparer.OrdinalIgnoreCase);

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
    internal (List<KnowledgeGraphNode> Nodes, List<RawEdge> Edges) ParseResponse(string json, Guid knowledgeBaseId)
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
                    var name = entity.GetProperty("name").GetString();
                    var type = entity.GetProperty("type").GetString();

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                        continue;

                    var node = new KnowledgeGraphNode
                    {
                        KnowledgeBaseId = knowledgeBaseId,
                        Name = name,
                        EntityType = type,
                        Description = entity.TryGetProperty("description", out var desc) ? desc.GetString() : null
                    };

                    nodes.Add(node);
                }
            }

            // 解析关系
            if (root.TryGetProperty("relationships", out var relsElement))
            {
                foreach (var rel in relsElement.EnumerateArray())
                {
                    var source = rel.GetProperty("source").GetString();
                    var target = rel.GetProperty("target").GetString();
                    var relation = rel.GetProperty("relation").GetString();

                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(relation))
                        continue;

                    double? weight = rel.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number
                        ? w.GetDouble()
                        : null;

                    edges.Add(new RawEdge(source, target, relation,
                        rel.TryGetProperty("description", out var edgeDesc) ? edgeDesc.GetString() : null,
                        weight));
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM graph extraction response as JSON");
        }

        return (nodes, edges);
    }

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
