using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// StreamingToolExecutor 流式工具执行测试
/// </summary>
public class StreamingToolExecutorTests
{
    private static readonly IList<AITool> EmptyTools = Array.Empty<AITool>();

    private StreamingToolExecutor CreateExecutor(
        Dictionary<string, ToolDefinition>? toolDefs = null,
        Func<FunctionCallContent, IList<AITool>, CancellationToken, Task<FunctionResultContent>>? executeTool = null)
    {
        var defs = toolDefs != null
            ? new Dictionary<string, ToolDefinition>(toolDefs, StringComparer.OrdinalIgnoreCase)
            : null;

        executeTool ??= async (fc, _, ct) =>
        {
            await Task.Delay(10, ct);
            return new FunctionResultContent(fc.CallId, $"result_{fc.Name}");
        };

        return new StreamingToolExecutor(
            defs,
            executeTool,
            NullLogger.Instance);
    }

    [Fact]
    public async Task AddTool_ShouldStartExecutionImmediately()
    {
        var executionStarted = new TaskCompletionSource();
        var executor = CreateExecutor(executeTool: async (fc, _, ct) =>
        {
            executionStarted.TrySetResult();
            await Task.Delay(100, ct);
            return new FunctionResultContent(fc.CallId, "done");
        });

        var toolCall = new FunctionCallContent("call_1", "read_file",
            new Dictionary<string, object?> { ["path"] = "test.txt" });
        executor.AddTool(toolCall, EmptyTools, CancellationToken.None);

        // 工具应立即开始执行
        var started = await Task.WhenAny(executionStarted.Task, Task.Delay(2000));
        started.ShouldBe(executionStarted.Task);
    }

    [Fact]
    public async Task GetAllResultsAsync_ReturnsInOrder()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>
        {
            ["tool_a"] = new() { Name = "tool_a", IsConcurrencySafe = true },
            ["tool_b"] = new() { Name = "tool_b", IsConcurrencySafe = true }
        };

        var executor = CreateExecutor(toolDefs);

        executor.AddTool(
            new FunctionCallContent("call_1", "tool_a", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);
        executor.AddTool(
            new FunctionCallContent("call_2", "tool_b", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);

        var results = await executor.GetAllResultsAsync(CancellationToken.None);

        results.Count.ShouldBe(2);
        results[0].CallId.ShouldBe("call_1");
        results[1].CallId.ShouldBe("call_2");
    }

    [Fact]
    public async Task AddTool_NonConcurrencySafe_BlocksSubsequent()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>
        {
            ["write_file"] = new() { Name = "write_file", IsConcurrencySafe = false },
            ["read_file"] = new() { Name = "read_file", IsConcurrencySafe = true }
        };

        var executionOrder = new List<string>();
        var executor = CreateExecutor(toolDefs, async (fc, _, ct) =>
        {
            lock (executionOrder) { executionOrder.Add($"start_{fc.Name}"); }
            await Task.Delay(50, ct);
            lock (executionOrder) { executionOrder.Add($"end_{fc.Name}"); }
            return new FunctionResultContent(fc.CallId, "ok");
        });

        executor.AddTool(
            new FunctionCallContent("call_1", "write_file", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);
        executor.AddTool(
            new FunctionCallContent("call_2", "read_file", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);

        // call_2 应该在 call_1 之后才开始
        executor.IsBlocked("call_2").ShouldBeTrue();

        var results = await executor.GetAllResultsAsync(CancellationToken.None);
        results.Count.ShouldBe(2);

        // write_file 应该在 read_file 之前完成
        var endWriteIdx = executionOrder.IndexOf("end_write_file");
        var startReadIdx = executionOrder.IndexOf("start_read_file");
        endWriteIdx.ShouldBeLessThan(startReadIdx);
    }

    [Fact]
    public async Task AddTool_AllConcurrencySafe_RunsInParallel()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>
        {
            ["search"] = new() { Name = "search", IsConcurrencySafe = true },
            ["read"] = new() { Name = "read", IsConcurrencySafe = true }
        };

        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var executor = CreateExecutor(toolDefs, async (fc, _, ct) =>
        {
            lock (lockObj) { concurrentCount++; maxConcurrent = Math.Max(maxConcurrent, concurrentCount); }
            await Task.Delay(50, ct);
            lock (lockObj) { concurrentCount--; }
            return new FunctionResultContent(fc.CallId, "ok");
        });

        executor.AddTool(
            new FunctionCallContent("call_1", "search", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);
        executor.AddTool(
            new FunctionCallContent("call_2", "read", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);

        await executor.GetAllResultsAsync(CancellationToken.None);

        // 两个并发安全工具应并行执行
        maxConcurrent.ShouldBe(2);
    }

    [Fact]
    public async Task AddTool_ExecutionFailure_ReturnsErrorResult()
    {
        var executor = CreateExecutor(executeTool: (fc, _, _) =>
            throw new InvalidOperationException("tool crashed"));

        executor.AddTool(
            new FunctionCallContent("call_1", "broken_tool", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);

        var results = await executor.GetAllResultsAsync(CancellationToken.None);
        results.Count.ShouldBe(1);
        (results[0].Result as string)!.ShouldContain("Tool execution failed");
    }

    [Fact]
    public void OnToolStarted_FiresWhenToolBegins()
    {
        var startedTools = new List<string>();
        var executor = CreateExecutor();
        executor.OnToolStarted += name => startedTools.Add(name);

        executor.AddTool(
            new FunctionCallContent("call_1", "test_tool", new Dictionary<string, object?>()),
            EmptyTools, CancellationToken.None);

        startedTools.ShouldContain("test_tool");
    }
}
