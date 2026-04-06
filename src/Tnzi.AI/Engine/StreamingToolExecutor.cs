namespace Tnzi.AI.Engine;

/// <summary>
/// 流式工具执行器 — 在 API 流式响应过程中，工具 block 一完整就立即开始执行。
/// 借鉴 Claude Code StreamingToolExecutor：状态机 queued→executing→completed→yielded。
/// </summary>
/// <remarks>
/// <para>工作流程：</para>
/// <para>1. 流式响应中 content_block_stop 触发 AddTool</para>
/// <para>2. 检查并发安全性（CanExecuteConcurrently），满足则立即启动执行</para>
/// <para>3. 不满足则入队等待前面的工具完成后顺序执行</para>
/// <para>4. GetAllResultsAsync 按添加顺序收集所有结果</para>
/// </remarks>
public class StreamingToolExecutor
{
    private readonly List<TrackedTool> _tools = [];
    private readonly object _lock = new();
    private readonly IReadOnlyDictionary<string, ToolDefinition>? _toolDefinitions;
    private readonly Func<FunctionCallContent, IList<AITool>, CancellationToken, Task<FunctionResultContent>> _executeTool;
    private readonly ILogger _logger;

    public event Action<string>? OnToolStarted;

    /// <summary>
    /// 初始化流式工具执行器
    /// </summary>
    /// <param name="toolDefinitions">工具定义字典（用于安全元数据查询）</param>
    /// <param name="executeTool">单工具执行委托（由 AgentExecutor 注入）</param>
    /// <param name="logger">日志记录器</param>
    public StreamingToolExecutor(
        IReadOnlyDictionary<string, ToolDefinition>? toolDefinitions,
        Func<FunctionCallContent, IList<AITool>, CancellationToken, Task<FunctionResultContent>> executeTool,
        ILogger logger)
    {
        _toolDefinitions = toolDefinitions;
        _executeTool = Check.NotNull(executeTool);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 添加一个已完整的工具调用（由 content_block_stop 事件触发）。
    /// 如果满足并发条件，立即开始执行。
    /// </summary>
    public void AddTool(FunctionCallContent toolCall, IList<AITool> allTools, CancellationToken ct)
    {
        var tracked = new TrackedTool(toolCall, allTools, ct);
        lock (_lock)
        {
            _tools.Add(tracked);
        }
        TryStartNext();
    }

    /// <summary>
    /// 获取所有已完成的结果（按添加顺序）。
    /// </summary>
    public async Task<List<FunctionResultContent>> GetAllResultsAsync(CancellationToken ct)
    {
        var results = new List<FunctionResultContent>();
        foreach (var tool in _tools)
        {
            results.Add(await tool.CompletionTask.WaitAsync(ct));
        }
        return results;
    }

    /// <summary>
    /// 检查指定工具是否因安全限制被阻塞
    /// </summary>
    public bool IsBlocked(string callId)
    {
        lock (_lock)
        {
            var tool = _tools.FirstOrDefault(t => t.Call.CallId == callId);
            return tool is { State: ToolState.Queued };
        }
    }

    /// <summary>
    /// 当前执行中的工具数
    /// </summary>
    public int ExecutingCount
    {
        get { lock (_lock) { return _tools.Count(t => t.State == ToolState.Executing); } }
    }

    private void TryStartNext()
    {
        lock (_lock)
        {
            // 单次遍历分类，避免双 LINQ 分配
            var hasExecuting = false;
            var allExecutingSafe = true;
            foreach (var t in _tools)
            {
                if (t.State == ToolState.Executing)
                {
                    hasExecuting = true;
                    if (!IsConcurrencySafe(t)) allExecutingSafe = false;
                }
            }

            foreach (var tool in _tools)
            {
                if (tool.State != ToolState.Queued) continue;

                var canStart = !hasExecuting || (allExecutingSafe && IsConcurrencySafe(tool));
                if (!canStart) break;

                tool.State = ToolState.Executing;
                hasExecuting = true;
                if (!IsConcurrencySafe(tool)) allExecutingSafe = false;
                _ = ExecuteToolAsync(tool);
                OnToolStarted?.Invoke(tool.Call.Name);
            }
        }
    }

    private bool IsConcurrencySafe(TrackedTool tool)
    {
        if (_toolDefinitions == null) return false;
        if (!_toolDefinitions.TryGetValue(tool.Call.Name, out var def)) return false;
        return def is { IsConcurrencySafe: true, IsDestructive: false };
    }

    private async Task ExecuteToolAsync(TrackedTool tool)
    {
        try
        {
            var result = await _executeTool(tool.Call, tool.AllTools, tool.CancellationToken);
            tool.SetResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming tool '{ToolName}' execution failed", tool.Call.Name);
            tool.SetResult(new FunctionResultContent(tool.Call.CallId,
                $"Tool execution failed: {ex.Message}"));
        }
        finally
        {
            lock (_lock) { tool.State = ToolState.Completed; }
            TryStartNext(); // 完成后尝试启动队列中的下一个
        }
    }

    private enum ToolState { Queued, Executing, Completed }

    private class TrackedTool(FunctionCallContent call, IList<AITool> allTools, CancellationToken ct)
    {
        public FunctionCallContent Call => call;
        public IList<AITool> AllTools => allTools;
        public CancellationToken CancellationToken => ct;
        public ToolState State { get; set; } = ToolState.Queued;

        private readonly TaskCompletionSource<FunctionResultContent> _tcs = new();
        public Task<FunctionResultContent> CompletionTask => _tcs.Task;

        public void SetResult(FunctionResultContent result) => _tcs.TrySetResult(result);
    }
}
