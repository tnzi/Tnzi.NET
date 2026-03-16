namespace Tnzi.AI.Tests;

/// <summary>
/// UsageAnalyticsService 集成测试 — 趋势统计和按 Agent 分组统计
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
            Granularity = "daily"
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
            Granularity = "monthly"
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
            Granularity = "weekly"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        // 两条记录在不同周
        result.Data.ShouldNotBeEmpty();
        result.Data.All(p => p.Period.StartsWith("2026-W")).ShouldBeTrue();
    }

    [Fact]
    public async Task GetUsageTrendAsync_EmptyData_ReturnsEmptyList()
    {
        var result = await Service.GetUsageTrendAsync(new UsageTrendQueryDto
        {
            StartTime = DateTime.UtcNow.AddDays(-7),
            EndTime = DateTime.UtcNow,
            Granularity = "daily"
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
            Granularity = "daily",
            Provider = "OpenAI"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Sum(p => p.TotalRequests).ShouldBe(1);
        result.Data.Sum(p => p.TotalTokens).ShouldBe(150);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("weekly")]
    [InlineData("monthly")]
    public async Task GetUsageTrendAsync_AllGranularities_ReturnSuccess(string granularity)
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

    #endregion
}
