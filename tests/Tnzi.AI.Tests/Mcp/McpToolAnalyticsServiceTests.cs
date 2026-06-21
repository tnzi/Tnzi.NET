using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Mcp.Services;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// McpToolAnalyticsService 单元测试 — P95 小样本边界、most-used / errors 聚合、
/// cleanup retentionDays 下限校验。查询路径用 MockQueryable 提供异步 EF 操作符。
/// </summary>
public class McpToolAnalyticsServiceTests
{
    private readonly Mock<IRepository<McpToolUsageRecord, long>> _repository = new();
    private readonly IServiceProvider _serviceProvider;

    public McpToolAnalyticsServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private McpToolAnalyticsService CreateService() => new(_serviceProvider, _repository.Object);

    private void SetupRecords(IEnumerable<McpToolUsageRecord> records)
    {
        var mock = records.ToList().BuildMock();
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private static McpToolUsageRecord Rec(
        string toolName,
        long durationMs,
        bool isSuccess = true,
        string? error = null,
        string? caller = null,
        DateTime? created = null) => new()
    {
        Id = Random.Shared.NextInt64(1, long.MaxValue),
        ToolName = toolName,
        DurationMs = durationMs,
        IsSuccess = isSuccess,
        ErrorMessage = error,
        CallerApiKeyId = caller,
        CreationTime = created ?? DateTime.UtcNow
    };

    // ─── GetToolStatsAsync — P95 small-sample boundary ───────────────────────

    [Fact]
    public async Task GetToolStatsAsync_NoRecords_ReturnsEmptyStatsForTool()
    {
        SetupRecords([]);
        var svc = CreateService();

        var result = await svc.GetToolStatsAsync("tool-a");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.ToolName.ShouldBe("tool-a");
        result.Data.TotalCalls.ShouldBe(0);
        result.Data.P95DurationMs.ShouldBe(0);
    }

    [Fact]
    public async Task GetToolStatsAsync_SingleRecord_P95IsThatRecordDuration()
    {
        // P95 of a 1-element set must be the single value (index = ceil(1*0.95)-1 = 0)
        SetupRecords([Rec("tool-a", durationMs: 42)]);
        var svc = CreateService();

        var result = await svc.GetToolStatsAsync("tool-a");

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCalls.ShouldBe(1);
        result.Data.P95DurationMs.ShouldBe(42);
        result.Data.AvgDurationMs.ShouldBe(42);
    }

    [Fact]
    public async Task GetToolStatsAsync_TwentyRecords_P95SelectsNineteenthOrdered()
    {
        // durations 1..20; ceil(20*0.95)-1 = 18 → 0-based index 18 → 19th smallest = 19
        var records = Enumerable.Range(1, 20).Select(i => Rec("tool-a", durationMs: i)).ToList();
        SetupRecords(records);
        var svc = CreateService();

        var result = await svc.GetToolStatsAsync("tool-a");

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCalls.ShouldBe(20);
        result.Data.P95DurationMs.ShouldBe(19);
    }

    [Fact]
    public async Task GetToolStatsAsync_ErrorRate_And_UniqueCallers_Computed()
    {
        SetupRecords([
            Rec("tool-a", 10, isSuccess: true, caller: "c1"),
            Rec("tool-a", 20, isSuccess: false, error: "boom", caller: "c1"),
            Rec("tool-a", 30, isSuccess: true, caller: "c2"),
            Rec("tool-a", 40, isSuccess: true, caller: "c2"),
            // a different tool must be excluded from tool-a stats
            Rec("tool-b", 99, isSuccess: false, error: "other", caller: "c3"),
        ]);
        var svc = CreateService();

        var result = await svc.GetToolStatsAsync("tool-a");

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCalls.ShouldBe(4);
        result.Data.ErrorRate.ShouldBe(0.25, 0.0001); // 1 of 4 failed
        result.Data.UniqueCallers.ShouldBe(2);         // c1, c2
    }

    // ─── GetMostUsedToolsAsync — aggregation + ordering ──────────────────────

    [Fact]
    public async Task GetMostUsedToolsAsync_OrdersByCallCountDescending_AndComputesSuccessRate()
    {
        SetupRecords([
            Rec("popular", 10, isSuccess: true),
            Rec("popular", 10, isSuccess: true),
            Rec("popular", 10, isSuccess: false),
            Rec("rare", 5, isSuccess: true),
        ]);
        var svc = CreateService();

        var result = await svc.GetMostUsedToolsAsync(top: 10);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data[0].ToolName.ShouldBe("popular");
        result.Data[0].CallCount.ShouldBe(3);
        result.Data[0].SuccessRate.ShouldBe(Math.Round(2.0 / 3.0, 4));
        result.Data[1].ToolName.ShouldBe("rare");
        result.Data[1].CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetMostUsedToolsAsync_RespectsTopLimit()
    {
        SetupRecords([
            Rec("a", 1), Rec("a", 1), Rec("a", 1),
            Rec("b", 1), Rec("b", 1),
            Rec("c", 1),
        ]);
        var svc = CreateService();

        var result = await svc.GetMostUsedToolsAsync(top: 2);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data.Select(d => d.ToolName).ShouldBe(["a", "b"]);
    }

    // ─── GetToolErrorsAsync — aggregation by error message ───────────────────

    [Fact]
    public async Task GetToolErrorsAsync_GroupsByMessage_OrdersByCount_ExcludesSuccesses()
    {
        SetupRecords([
            Rec("tool-a", 10, isSuccess: false, error: "timeout"),
            Rec("tool-a", 10, isSuccess: false, error: "timeout"),
            Rec("tool-a", 10, isSuccess: false, error: "bad-input"),
            Rec("tool-a", 10, isSuccess: true),               // success → excluded
            Rec("tool-a", 10, isSuccess: false, error: null), // null error → excluded
            Rec("tool-b", 10, isSuccess: false, error: "other-tool"), // wrong tool → excluded
        ]);
        var svc = CreateService();

        var result = await svc.GetToolErrorsAsync("tool-a");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data[0].ErrorMessage.ShouldBe("timeout");
        result.Data[0].Count.ShouldBe(2);
        result.Data[1].ErrorMessage.ShouldBe("bad-input");
        result.Data[1].Count.ShouldBe(1);
    }

    // ─── CleanupOldRecordsAsync — retentionDays lower-bound guard ────────────

    [Fact]
    public async Task CleanupOldRecordsAsync_ZeroRetention_RejectedWithoutTouchingRepository()
    {
        var svc = CreateService();

        var result = await svc.CleanupOldRecordsAsync(retentionDays: 0);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        // Guard must short-circuit before issuing any delete query
        _repository.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOldRecordsAsync_NegativeRetention_Rejected()
    {
        var svc = CreateService();

        var result = await svc.CleanupOldRecordsAsync(retentionDays: -5);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _repository.Verify(r => r.AsQueryable(It.IsAny<bool>()), Times.Never);
    }
}
