namespace Tnzi.AI.Tests;

/// <summary>
/// UsageAnalyticsService 集成测试 - 趋势统计和按 Agent 分组统计
/// 使用 SQLite 内存数据库以支持 EF Core 异步查询 (ToListAsync)
/// </summary>
public class UsageAnalyticsServiceTests : AIAnalyticsTestBase
{
    #region GetUsageTrendAsync

    [Fact]
    public async Task GetUsageTrendAsync_Daily_ReturnsDailyPoints()
    {
        var now = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);
        await SeedLogsAsync([
            new() { CreationTime = now.AddDays(-2), InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now.AddDays(-2), InputTokens = 200, OutputTokens = 80, TotalTokens = 280, DurationMs = 300, IsSuccess = false, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now.AddDays(-1), InputTokens = 150, OutputTokens = 60, TotalTokens = 210, DurationMs = 250, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = now.AddDays(-3),
            EndTime = now,
            Granularity = UsageGranularity.Daily
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(2); // 两天有数据

        var day1 = result.Data[0]; // 倒数第二天
        day1.TotalRequests.ShouldBe(2);
        day1.SuccessfulRequests.ShouldBe(1);
        day1.TotalInputTokens.ShouldBe(300);
        day1.TotalOutputTokens.ShouldBe(130);
        day1.TotalTokens.ShouldBe(430);

        var day2 = result.Data[1]; // 昨天
        day2.TotalRequests.ShouldBe(1);
        day2.SuccessfulRequests.ShouldBe(1);
    }

    [Fact]
    public async Task GetUsageTrendAsync_Monthly_ReturnsMonthlyPoints()
    {
        await SeedLogsAsync([
            new() { CreationTime = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), InputTokens = 200, OutputTokens = 80, TotalTokens = 280, DurationMs = 300, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), InputTokens = 150, OutputTokens = 60, TotalTokens = 210, DurationMs = 250, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
            Granularity = UsageGranularity.Monthly
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(2);
        result.Data[0].Period.ShouldBe("2026-01");
        result.Data[0].TotalRequests.ShouldBe(2);
        result.Data[0].TotalTokens.ShouldBe(430);
        result.Data[1].Period.ShouldBe("2026-02");
        result.Data[1].TotalRequests.ShouldBe(1);
    }

    [Fact]
    public async Task GetUsageTrendAsync_Weekly_ReturnsWeeklyPoints()
    {
        await SeedLogsAsync([
            new() { CreationTime = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), InputTokens = 200, OutputTokens = 80, TotalTokens = 280, DurationMs = 300, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc),
            Granularity = UsageGranularity.Weekly
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        // 两条记录在不同周
        result.Data.ShouldNotBeEmpty();
        result.Data.All(p => p.Period.StartsWith("2026-W")).ShouldBeTrue();
    }

    [Fact]
    public async Task GetUsageTrendAsync_Weekly_UsesIsoWeeksStartingOnMonday()
    {
        // 2026-03-08 是周日、03-09 是周一：ISO 周在两天之间换周（此前按 DayOfYear/7 分在同一桶里）
        await SeedLogsAsync([
            Log(new DateTime(2026, 3, 8, 10, 0, 0, DateTimeKind.Utc), tokens: 100, durationMs: 100),
            Log(new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc), tokens: 200, durationMs: 300),
            Log(new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc), tokens: 300, durationMs: 500),
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            Granularity = UsageGranularity.Weekly
        });

        result.Succeeded.ShouldBeTrue();
        var points = result.Data!;
        points.Select(p => p.Period).ShouldBe(["2026-W10", "2026-W11"]);
        points[0].PeriodStart.ShouldBe(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));
        points[1].PeriodStart.ShouldBe(new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc));
        points[1].TotalRequests.ShouldBe(2);
        points[1].TotalTokens.ShouldBe(500);
        // 周平均按请求数加权，不是两天日平均的平均
        points[1].AverageDurationMs.ShouldBe(400);
    }

    [Fact]
    public async Task GetUsageTrendAsync_Weekly_YearEndWeekSpansBothYears()
    {
        // ISO 2026 年有 53 周：W53 是 2026-12-28（周一）到 2027-01-03。此前 12-31 落进一个只有一天的「W53」，
        // 01-01 进 2027 年的「W01」
        await SeedLogsAsync([
            Log(new DateTime(2026, 12, 31, 10, 0, 0, DateTimeKind.Utc), tokens: 100, durationMs: 100),
            Log(new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc), tokens: 100, durationMs: 100),
            // 2025-12-31 属于 ISO 2026 年第 1 周，不该出现一个只有一天的 2025 年第 53 周
            Log(new DateTime(2025, 12, 31, 10, 0, 0, DateTimeKind.Utc), tokens: 100, durationMs: 100),
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            Granularity = UsageGranularity.Weekly
        });

        result.Succeeded.ShouldBeTrue();
        var points = result.Data!;
        points.Select(p => p.Period).ShouldBe(["2026-W01", "2026-W53"]);
        points[0].PeriodStart.ShouldBe(new DateTime(2025, 12, 29, 0, 0, 0, DateTimeKind.Utc));
        points[1].PeriodStart.ShouldBe(new DateTime(2026, 12, 28, 0, 0, 0, DateTimeKind.Utc));
        points[1].TotalRequests.ShouldBe(2);
    }

    private static UsageLog Log(DateTime at, long tokens, long durationMs) => new()
    {
        CreationTime = at,
        InputTokens = (int)tokens,
        OutputTokens = 0,
        TotalTokens = (int)tokens,
        DurationMs = durationMs,
        IsSuccess = true,
        Provider = "OpenAI",
        Model = "gpt-4o"
    };

    [Fact]
    public async Task GetUsageTrendAsync_EmptyData_ReturnsEmptyList()
    {
        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = DateTime.UtcNow.AddDays(-7),
            EndTime = DateTime.UtcNow,
            Granularity = UsageGranularity.Daily
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUsageTrendAsync_WithProviderFilter_FiltersCorrectly()
    {
        var now = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);
        await SeedLogsAsync([
            new() { CreationTime = now, InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, InputTokens = 200, OutputTokens = 80, TotalTokens = 280, DurationMs = 300, IsSuccess = true, Provider = "Anthropic", Model = "claude-3" },
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = now.AddDays(-1),
            EndTime = now.AddDays(1),
            Granularity = UsageGranularity.Daily,
            Provider = "OpenAI"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Sum(p => p.TotalRequests).ShouldBe(1);
        result.Data.Sum(p => p.TotalTokens).ShouldBe(150);
    }

    [Theory]
    [InlineData(UsageGranularity.Daily)]
    [InlineData(UsageGranularity.Weekly)]
    [InlineData(UsageGranularity.Monthly)]
    public async Task GetUsageTrendAsync_AllGranularities_ReturnSuccess(UsageGranularity granularity)
    {
        var now = DateTime.UtcNow;
        await SeedLogsAsync([
            new() { CreationTime = now, InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
        ]);

        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = now.AddDays(-1),
            EndTime = now.AddDays(1),
            Granularity = granularity
        });

        result.Succeeded.ShouldBeTrue();
    }

    #endregion

    #region GetUsageByAgentAsync

    [Fact]
    public async Task GetUsageByAgentAsync_ReturnsTopNByTokenConsumption()
    {
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        var agentId3 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await SeedAgentsAsync([
            new() { Id = agentId1, Name = "CodeReviewer", Provider = "OpenAI" },
            new() { Id = agentId2, Name = "Summarizer", Provider = "OpenAI" },
            new() { Id = agentId3, Name = "Planner", Provider = "OpenAI" },
        ]);
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId1, InputTokens = 1000, OutputTokens = 500, TotalTokens = 1500, DurationMs = 300, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, AgentId = agentId1, InputTokens = 800, OutputTokens = 400, TotalTokens = 1200, DurationMs = 250, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, AgentId = agentId2, InputTokens = 200, OutputTokens = 100, TotalTokens = 300, DurationMs = 100, IsSuccess = false, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, AgentId = agentId3, InputTokens = 500, OutputTokens = 250, TotalTokens = 750, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
        ]);

        var result = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1));

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(3);

        // 按 TotalTokens 降序排列
        result.Data[0].AgentId.ShouldBe(agentId1);
        result.Data[0].AgentName.ShouldBe("CodeReviewer");
        result.Data[0].TotalRequests.ShouldBe(2);
        result.Data[0].TotalTokens.ShouldBe(2700);
        result.Data[0].SuccessRate.ShouldBe(1.0);

        result.Data[1].AgentId.ShouldBe(agentId3);
        result.Data[1].AgentName.ShouldBe("Planner");
        result.Data[1].TotalTokens.ShouldBe(750);

        result.Data[2].AgentId.ShouldBe(agentId2);
        result.Data[2].AgentName.ShouldBe("Summarizer");
        result.Data[2].SuccessRate.ShouldBe(0.0);
    }

    [Fact]
    public async Task GetUsageByAgentAsync_ExcludesLogsWithoutAgentId()
    {
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await SeedAgentsAsync([new() { Id = agentId, Name = "TestAgent", Provider = "OpenAI" }]);
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId, InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, AgentId = null, InputTokens = 200, OutputTokens = 100, TotalTokens = 300, DurationMs = 300, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" }, // direct chat, no agent
        ]);

        var result = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1));

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1); // only agent-attributed logs
    }

    [Fact]
    public async Task GetUsageByAgentAsync_RespectsTopNLimit()
    {
        var now = DateTime.UtcNow;
        var logs = Enumerable.Range(1, 25)
            .Select(i => new UsageLog
            {
                CreationTime = now,
                AgentId = Guid.NewGuid(),
                InputTokens = i * 10,
                OutputTokens = i * 5,
                TotalTokens = i * 15,
                DurationMs = 100,
                IsSuccess = true,
                Provider = "OpenAI",
                Model = "gpt-4o"
            })
            .ToList();
        await SeedLogsAsync(logs);

        var result = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1), topN: 10);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public async Task GetUsageByAgentAsync_AgentNameFallsBackToUnknownWhenMissing()
    {
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // 日志中有 AgentId，但 Agent 表中没有对应记录
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId, InputTokens = 100, OutputTokens = 50, TotalTokens = 150, DurationMs = 200, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" }
        ]);

        var result = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1));

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(1);
        result.Data[0].AgentName.ShouldBe("Unknown");
    }

    [Fact]
    public async Task GetUsageByAgentAsync_EmptyData_ReturnsEmptyList()
    {
        var result = await Service.GetUsageByAgentAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.ShouldBeEmpty();
    }

    // The dashboard's by-agent pie is a cost share: the DTO carried the field but the
    // aggregation never summed it, so every agent read $0 and the pie had nothing to draw.
    [Fact]
    public async Task GetUsageByAgentAsync_SumsEstimatedCostPerAgent()
    {
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedAgentsAsync([
            new() { Id = agentId1, Name = "CodeReviewer", Provider = "OpenAI" },
            new() { Id = agentId2, Name = "Summarizer", Provider = "OpenAI" },
        ]);
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId1, TotalTokens = 100, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o", EstimatedCostUsd = 0.25m },
            new() { CreationTime = now, AgentId = agentId1, TotalTokens = 100, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o", EstimatedCostUsd = 0.50m },
            new() { CreationTime = now, AgentId = agentId2, TotalTokens = 50, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o", EstimatedCostUsd = null },
        ]);

        var result = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1));

        result.Data.ShouldNotBeNull();
        result.Data.Single(a => a.AgentId == agentId1).TotalEstimatedCostUsd.ShouldBe(0.75m);
        result.Data.Single(a => a.AgentId == agentId2).TotalEstimatedCostUsd.ShouldBe(0m);
    }

    // The dashboard filters by provider and agent; a breakdown that ignored them
    // contradicted the KPIs and trend drawn from the same filters.
    [Fact]
    public async Task GetUsageByAgentAsync_AppliesProviderModelAndAgentFilters()
    {
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedAgentsAsync([
            new() { Id = agentId1, Name = "CodeReviewer", Provider = "OpenAI" },
            new() { Id = agentId2, Name = "Summarizer", Provider = "OpenAI" },
        ]);
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId1, TotalTokens = 100, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, AgentId = agentId1, TotalTokens = 300, DurationMs = 10, IsSuccess = true, Provider = "Anthropic", Model = "claude" },
            new() { CreationTime = now, AgentId = agentId2, TotalTokens = 200, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o-mini" },
        ]);

        var byProvider = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1), provider: "OpenAI");
        byProvider.Data!.Select(a => (a.AgentId, a.TotalTokens)).ShouldBe([(agentId2, 200L), (agentId1, 100L)]);

        var byModel = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1), model: "gpt-4o");
        byModel.Data!.Select(a => a.AgentId).ShouldBe([agentId1]);

        var byAgent = await Service.GetUsageByAgentAsync(now.AddDays(-1), now.AddDays(1), agentId: agentId1);
        byAgent.Data!.Single().TotalTokens.ShouldBe(400);
    }

    #endregion

    #region GetUsageByModelAsync / GetCostSummaryAsync

    [Fact]
    public async Task GetUsageByModelAsync_AppliesAgentFilter()
    {
        var agentId1 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId1, TotalTokens = 100, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o" },
            new() { CreationTime = now, AgentId = Guid.NewGuid(), TotalTokens = 900, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o-mini" },
        ]);

        var result = await Service.GetUsageByModelAsync(now.AddDays(-1), now.AddDays(1), agentId: agentId1);

        result.Data!.Select(m => (m.Model, m.TotalTokens)).ShouldBe([("gpt-4o", 100L)]);
    }

    [Fact]
    public async Task GetCostSummaryAsync_AppliesAgentFilter()
    {
        var agentId1 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedLogsAsync([
            new() { CreationTime = now, AgentId = agentId1, TotalTokens = 100, DurationMs = 10, IsSuccess = true, Provider = "OpenAI", Model = "gpt-4o", EstimatedCostUsd = 0.40m },
            new() { CreationTime = now, AgentId = Guid.NewGuid(), TotalTokens = 900, DurationMs = 10, IsSuccess = true, Provider = "Anthropic", Model = "claude", EstimatedCostUsd = 2.00m },
        ]);

        var result = await Service.GetCostSummaryAsync(now.AddDays(-1), now.AddDays(1), agentId: agentId1);

        result.Data!.TotalCostUsd.ShouldBe(0.40m);
        result.Data.TotalRequests.ShouldBe(1);
        result.Data.ByProvider.Select(p => p.Provider).ShouldBe(["OpenAI"]);
    }

    #endregion
}
