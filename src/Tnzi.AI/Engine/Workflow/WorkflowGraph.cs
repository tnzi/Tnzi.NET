namespace Tnzi.AI.Engine.Workflow;

/// <summary>
/// 工作流图 — 纯数据结构，表示节点和边的有向图（含条件边和循环支持）
/// </summary>
public class WorkflowGraph
{
    private readonly Dictionary<string, WorkflowStepDto> _nodeMap;
    private readonly Dictionary<string, List<string>> _adjacency;
    private readonly Dictionary<string, List<string>> _reverseAdjacency;
    private readonly List<string> _topologicalOrder;

    /// <summary>所有节点定义</summary>
    public IReadOnlyList<WorkflowStepDto> Nodes { get; }

    /// <summary>条件边列表</summary>
    public IReadOnlyList<ConditionalEdge> ConditionalEdges { get; }

    /// <summary>循环定义（Key: 循环 ID, Value: 循环内的节点 ID 集合 + 最大迭代次数）</summary>
    public IReadOnlyDictionary<string, LoopDefinition> Loops { get; }

    public WorkflowGraph(
        IReadOnlyList<WorkflowStepDto> nodes,
        IReadOnlyList<ConditionalEdge>? conditionalEdges = null,
        IReadOnlyDictionary<string, LoopDefinition>? loops = null)
    {
        Nodes = Check.NotNullOrEmpty(nodes).ToList();
        ConditionalEdges = conditionalEdges ?? [];
        Loops = loops ?? new Dictionary<string, LoopDefinition>();

        // 构建节点映射
        _nodeMap = new Dictionary<string, WorkflowStepDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var nodeId = node.StepId ?? throw new InvalidOperationException("All workflow nodes must have a StepId.");
            if (!_nodeMap.TryAdd(nodeId, node))
            {
                throw new InvalidOperationException($"Duplicate node ID: '{nodeId}'.");
            }
        }

        // 构建邻接表和反向邻接表
        _adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _reverseAdjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            var nodeId = node.StepId!;
            _adjacency.TryAdd(nodeId, []);
            _reverseAdjacency.TryAdd(nodeId, []);
        }

        foreach (var node in nodes)
        {
            if (node.DependsOn == null) continue;
            foreach (var dep in node.DependsOn)
            {
                if (!_nodeMap.ContainsKey(dep))
                {
                    throw new InvalidOperationException($"Node '{node.StepId}' depends on unknown node '{dep}'.");
                }

                _adjacency[dep].Add(node.StepId!);
                _reverseAdjacency[node.StepId!].Add(dep);
            }
        }

        // 循环检测（对非循环边）并生成拓扑排序
        _topologicalOrder = ComputeTopologicalOrder();
    }

    /// <summary>
    /// 获取节点定义
    /// </summary>
    public WorkflowStepDto? GetNode(string nodeId)
    {
        return _nodeMap.GetValueOrDefault(nodeId);
    }

    /// <summary>
    /// 获取当前可执行的节点（所有依赖已在 completedNodes 中）
    /// </summary>
    public IReadOnlyList<WorkflowStepDto> GetReadyNodes(IReadOnlySet<string> completedNodes)
    {
        Check.NotNull(completedNodes);

        var ready = new List<WorkflowStepDto>();
        foreach (var node in Nodes)
        {
            var nodeId = node.StepId!;
            if (completedNodes.Contains(nodeId)) continue;

            var deps = _reverseAdjacency.GetValueOrDefault(nodeId, []);
            if (deps.All(d => completedNodes.Contains(d)))
            {
                ready.Add(node);
            }
        }

        return ready;
    }

    /// <summary>
    /// 获取节点的上游依赖节点 ID
    /// </summary>
    public IReadOnlyList<string> GetDependencies(string nodeId)
    {
        return _reverseAdjacency.GetValueOrDefault(nodeId, []);
    }

    /// <summary>
    /// 获取节点的下游节点 ID
    /// </summary>
    public IReadOnlyList<string> GetDownstream(string nodeId)
    {
        return _adjacency.GetValueOrDefault(nodeId, []);
    }

    /// <summary>
    /// 获取节点的所有传递下游节点 ID（BFS，包含间接下游）
    /// </summary>
    public IReadOnlySet<string> GetTransitiveDownstream(string nodeId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var direct in GetDownstream(nodeId))
            queue.Enqueue(direct);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!result.Add(current)) continue;
            foreach (var child in GetDownstream(current))
                queue.Enqueue(child);
        }
        return result;
    }

    /// <summary>
    /// 获取拓扑排序结果
    /// </summary>
    public IReadOnlyList<string> GetTopologicalOrder() => _topologicalOrder;

    /// <summary>
    /// 获取指定节点的条件边（如果有）
    /// </summary>
    public ConditionalEdge? GetConditionalEdge(string fromNodeId)
    {
        return ConditionalEdges.FirstOrDefault(e =>
            string.Equals(e.FromNodeId, fromNodeId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断节点是否在循环内
    /// </summary>
    public (bool InLoop, string? LoopId) IsInLoop(string nodeId)
    {
        foreach (var (loopId, loopDef) in Loops)
        {
            if (loopDef.NodeIds.Contains(nodeId))
            {
                return (true, loopId);
            }
        }
        return (false, null);
    }

    /// <summary>
    /// Kahn 算法拓扑排序（检测环路）
    /// </summary>
    private List<string> ComputeTopologicalOrder()
    {
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Nodes)
        {
            inDegree[node.StepId!] = _reverseAdjacency.GetValueOrDefault(node.StepId!, []).Count;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);

            foreach (var next in _adjacency.GetValueOrDefault(current, []))
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        // 如果有循环定义，允许循环内节点不在拓扑排序中
        var loopNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loopDef in Loops.Values)
        {
            foreach (var nodeId in loopDef.NodeIds)
            {
                loopNodeIds.Add(nodeId);
            }
        }

        var nonLoopNodeCount = Nodes.Count(n => !loopNodeIds.Contains(n.StepId!));
        var processedNonLoopCount = order.Count(id => !loopNodeIds.Contains(id));

        if (processedNonLoopCount != nonLoopNodeCount)
        {
            throw new InvalidOperationException("Workflow contains circular dependencies outside of defined loops.");
        }

        // 将循环节点按定义顺序追加到拓扑排序结果中
        foreach (var loopDef in Loops.Values)
        {
            foreach (var nodeId in loopDef.NodeIds)
            {
                if (!order.Contains(nodeId))
                {
                    order.Add(nodeId);
                }
            }
        }

        return order;
    }
}

/// <summary>
/// 循环定义 — 工作流中的循环结构
/// </summary>
public class LoopDefinition
{
    /// <summary>循环内的节点 ID 集合（按执行顺序）</summary>
    public required IReadOnlyList<string> NodeIds { get; init; }

    /// <summary>最大迭代次数（防止无限循环，默认 5）</summary>
    public int MaxIterations { get; init; } = 5;
}
