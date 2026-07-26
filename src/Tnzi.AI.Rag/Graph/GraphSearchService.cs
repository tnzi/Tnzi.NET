namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 知识图谱搜索服务 - 通过实体名称/类型匹配 + 1-hop 关系扩展实现图谱检索
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>检索方式（名实相符说明）：</b>本服务基于实体<b>名称/类型的关键词匹配</b>（精确 / 部分 / 类型匹配，
/// 见 <see cref="CalculateScore"/>）+ 1-hop 关系扩展，<b>不是</b>基于节点向量的语义相似度检索。
/// 要实现真正的语义 GraphRAG（跨文档实体语义关联推理），需为 <see cref="KnowledgeGraphNode"/> 增加
/// embedding 列、在摄取时生成节点向量、并改用向量匹配 —— 属未来增强，当前不具备。请勿将其等同于
/// 向量语义图谱检索。
/// </para>
/// <para>
/// 搜索管道：
/// 1. 按查询文本对知识库内所有节点进行名称/类型匹配，计算匹配得分
/// 2. 对每个匹配节点加载 1-hop 关联边和目标节点
/// 3. 构建上下文片段（Entity + Related 关系列表）
/// 4. 按得分降序截取 topK
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Graph search is in preview")]
public class GraphSearchService : IGraphSearchService
{
    private readonly IRepository<KnowledgeGraphNode, Guid> _nodeRepository;
    private readonly IRepository<KnowledgeGraphEdge, Guid> _edgeRepository;
    private readonly ILogger<GraphSearchService> _logger;

    /// <summary>精确名称匹配得分</summary>
    private const double ExactMatchScore = 1.0;

    /// <summary>名称部分匹配得分（name 包含 query 或 query 包含 name）</summary>
    private const double PartialMatchScore = 0.7;

    /// <summary>仅类型匹配得分</summary>
    private const double TypeMatchScore = 0.3;

    public GraphSearchService(
        IRepository<KnowledgeGraphNode, Guid> nodeRepository,
        IRepository<KnowledgeGraphEdge, Guid> edgeRepository,
        ILogger<GraphSearchService> logger)
    {
        _nodeRepository = Check.NotNull(nodeRepository);
        _edgeRepository = Check.NotNull(edgeRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphSearchResult>> SearchAsync(
        string query, Guid knowledgeBaseId, GraphSearchOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        options ??= new GraphSearchOptions();
        var queryLower = query.ToLower();

        try
        {
            // 1. 查询知识库内匹配节点（限制候选数量防止大图全加载）
            //    注意：候选过滤谓词与 CalculateScore 打分谓词在语义上等价（同为大小写不敏感的
            //    精确/部分/类型匹配），但写法不同是有意为之——此处在 SQL 中执行，须用 .ToLower()
            //    才能翻译为数据库 LOWER()（query 已预先小写为 queryLower）；CalculateScore 在内存中
            //    执行，改用 StringComparison.OrdinalIgnoreCase（框架编码规则 #16：EF LINQ 用 ToLower，
            //    内存比较用 OrdinalIgnoreCase）。两者覆盖的命中集合一致。
            var candidateLimit = Math.Max(options.MaxResults * 10, 50);
            var candidateNodes = await _nodeRepository.AsQueryable()
                .Where(n => n.KnowledgeBaseId == knowledgeBaseId
                    && (n.Name.ToLower().Contains(queryLower)
                        || queryLower.Contains(n.Name.ToLower())
                        || n.EntityType.ToLower().Contains(queryLower)))
                .Take(candidateLimit)
                .ToListAsync(ct);

            if (candidateNodes.Count == 0)
            {
                _logger.LogDebug("Graph search found no matching nodes for query '{Query}' in KB {KbId}",
                    query, knowledgeBaseId);
                return [];
            }

            // 2. 计算每个节点的匹配得分
            var scoredNodes = candidateNodes
                .Select(node => (Node: node, Score: CalculateScore(node, queryLower)))
                .OrderByDescending(x => x.Score)
                .Take(options.MaxResults)
                .ToList();

            // 3. 加载关联边和目标节点（1-hop）
            var nodeIds = scoredNodes.Select(x => x.Node.Id).ToHashSet();
            Dictionary<Guid, List<(KnowledgeGraphEdge Edge, KnowledgeGraphNode TargetNode, RelationDirection Direction)>>? edgesByNode = null;

            if (options.IncludeRelationships)
            {
                edgesByNode = await LoadRelatedEdgesAsync(nodeIds, knowledgeBaseId, ct);
            }

            // 4. 构建搜索结果
            var results = new List<GraphSearchResult>(scoredNodes.Count);

            foreach (var (node, score) in scoredNodes)
            {
                var related = new List<RelatedNodeInfo>();

                if (edgesByNode != null && edgesByNode.TryGetValue(node.Id, out var edges))
                {
                    related = edges.Select(e => new RelatedNodeInfo(
                        e.TargetNode.Name,
                        e.TargetNode.EntityType,
                        e.Edge.RelationType,
                        e.Direction)).ToList();
                }

                var contextSnippet = BuildContextSnippet(node, related);

                results.Add(new GraphSearchResult(
                    node.Name,
                    node.EntityType,
                    related,
                    score,
                    contextSnippet));
            }

            _logger.LogDebug(
                "Graph search returned {Count} results for query '{Query}' in KB {KbId}",
                results.Count, query, knowledgeBaseId);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Graph search failed for query '{Query}' in KB {KbId}", query, knowledgeBaseId);
            return [];
        }
    }

    /// <summary>
    /// 计算节点与查询的匹配得分
    /// </summary>
    private static double CalculateScore(KnowledgeGraphNode node, string queryLower)
    {
        // 精确匹配（名称完全等于查询）
        if (node.Name.Equals(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            return ExactMatchScore;
        }

        // 部分匹配（名称包含查询，或查询包含名称）
        if (node.Name.Contains(queryLower, StringComparison.OrdinalIgnoreCase)
            || queryLower.Contains(node.Name, StringComparison.OrdinalIgnoreCase))
        {
            return PartialMatchScore;
        }

        // 仅类型匹配
        if (node.EntityType.Contains(queryLower, StringComparison.OrdinalIgnoreCase))
        {
            return TypeMatchScore;
        }

        return 0;
    }

    /// <summary>
    /// 加载指定节点的 1-hop 关联边和目标节点
    /// </summary>
    private async Task<Dictionary<Guid, List<(KnowledgeGraphEdge Edge, KnowledgeGraphNode TargetNode, RelationDirection Direction)>>>
        LoadRelatedEdgesAsync(HashSet<Guid> nodeIds, Guid knowledgeBaseId, CancellationToken ct)
    {
        // 查询所有相关边（源或目标在 nodeIds 中）
        var edges = await _edgeRepository.AsQueryable()
            .Where(e => e.KnowledgeBaseId == knowledgeBaseId
                && (nodeIds.Contains(e.SourceNodeId) || nodeIds.Contains(e.TargetNodeId)))
            .ToListAsync(ct);

        if (edges.Count == 0)
        {
            return new Dictionary<Guid, List<(KnowledgeGraphEdge, KnowledgeGraphNode, RelationDirection)>>();
        }

        // 收集需要加载的关联节点 ID（不在 nodeIds 中的节点）
        var relatedNodeIds = edges
            .SelectMany(e => new[] { e.SourceNodeId, e.TargetNodeId })
            .Except(nodeIds)
            .Distinct()
            .ToList();

        // 加载所有关联节点（包括 nodeIds 自身，用于边指向同组节点的情况）
        var allNodeIds = nodeIds.Concat(relatedNodeIds).Distinct().ToList();
        var nodeMap = await _nodeRepository.AsQueryable()
            .Where(n => allNodeIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, ct);

        // 按节点 ID 分组边
        var result = new Dictionary<Guid, List<(KnowledgeGraphEdge, KnowledgeGraphNode, RelationDirection)>>();

        foreach (var edge in edges)
        {
            // 处理 outgoing 方向（当前节点为源）
            if (nodeIds.Contains(edge.SourceNodeId) && nodeMap.TryGetValue(edge.TargetNodeId, out var targetNode))
            {
                if (!result.ContainsKey(edge.SourceNodeId))
                {
                    result[edge.SourceNodeId] = [];
                }

                result[edge.SourceNodeId].Add((edge, targetNode, RelationDirection.Outgoing));
            }

            // 处理 incoming 方向（当前节点为目标）
            if (nodeIds.Contains(edge.TargetNodeId) && nodeMap.TryGetValue(edge.SourceNodeId, out var sourceNode))
            {
                if (!result.ContainsKey(edge.TargetNodeId))
                {
                    result[edge.TargetNodeId] = [];
                }

                result[edge.TargetNodeId].Add((edge, sourceNode, RelationDirection.Incoming));
            }
        }

        return result;
    }

    /// <summary>
    /// 构建上下文片段：Entity 描述 + 关系列表
    /// </summary>
    private static string BuildContextSnippet(KnowledgeGraphNode node, List<RelatedNodeInfo> relatedNodes)
    {
        var sb = new StringBuilder();
        sb.Append($"Entity: {node.Name} ({node.EntityType})");

        if (!string.IsNullOrWhiteSpace(node.Description))
        {
            sb.Append($" - {node.Description}");
        }

        sb.Append('.');

        if (relatedNodes.Count > 0)
        {
            sb.Append(" Related: ");
            var relations = relatedNodes.Select(r => r.Direction == RelationDirection.Outgoing
                ? $"{r.RelationType} → {r.NodeName}"
                : $"{r.NodeName} → {r.RelationType}");
            sb.Append(string.Join(", ", relations));
            sb.Append('.');
        }

        return sb.ToString();
    }
}
