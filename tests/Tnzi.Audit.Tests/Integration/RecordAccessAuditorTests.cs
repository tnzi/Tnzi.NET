using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 记录级读取审计：哈希链完整性、导出配额、未启用时的零影响。
/// </summary>
public class RecordAccessAuditorTests : IntegrationTestBase
{
    /// <summary>取实际落库条目上的 UserId：测试宿主的当前用户可能是匿名（null）。</summary>
    private async Task<Guid?> CurrentChainOwnerAsync()
        => (await DbContext.Set<AuditRecordAccess>().FirstOrDefaultAsync())?.UserId;

    private RecordAccessAuditor CreateAuditor(RecordAccessAuditOptions options)
    {
        var monitor = new StaticOptionsMonitor<RecordAccessAuditOptions>(options);
        var repository = new EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>(
            DbContext, serviceProvider: ServiceProvider);
        return new RecordAccessAuditor(repository, monitor, ServiceProvider);
    }

    private static RecordAccessAuditOptions Enabled(int quota = 0) => new()
    {
        Enabled = true,
        MaxReadsPerUserPerHour = quota
    };

    [Fact]
    public async Task Disabled_RecordsNothingAndSucceeds()
    {
        // 「可选能力」的核心断言：不启用就什么都不写，调用方也不必判断开关。
        var auditor = CreateAuditor(new RecordAccessAuditOptions { Enabled = false });

        var result = await auditor.RecordAsync("Tip", "abc", "case-review");

        Assert.True(result.Succeeded);
        Assert.Empty(await DbContext.Set<AuditRecordAccess>().ToListAsync());
    }

    [Fact]
    public async Task Record_WritesEntryWithResourceAndPurpose()
    {
        var auditor = CreateAuditor(Enabled());

        var result = await auditor.RecordAsync("Tip", "tip-42", "case-review");

        Assert.True(result.Succeeded);
        var entry = Assert.Single(await DbContext.Set<AuditRecordAccess>().ToListAsync());
        Assert.Equal("Tip", entry.ResourceType);
        Assert.Equal("tip-42", entry.ResourceId);
        Assert.Equal("case-review", entry.Purpose);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(string.Empty, entry.PreviousHash);
        Assert.NotEmpty(entry.Hash);
    }

    [Fact]
    public async Task ConsecutiveReads_FormAHashChain()
    {
        var auditor = CreateAuditor(Enabled());

        await auditor.RecordAsync("Tip", "1");
        await auditor.RecordAsync("Tip", "2");
        await auditor.RecordAsync("Tip", "3");

        var entries = await DbContext.Set<AuditRecordAccess>()
            .OrderBy(e => e.Sequence).ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal([1L, 2L, 3L], entries.Select(e => e.Sequence));
        // 每一条都链到上一条
        Assert.Equal(string.Empty, entries[0].PreviousHash);
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
        Assert.Equal(entries[1].Hash, entries[2].PreviousHash);
    }

    [Fact]
    public async Task VerifyChain_PassesOnIntactChain()
    {
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "1");
        await auditor.RecordAsync("Tip", "2");

        Assert.True((await auditor.VerifyChainAsync(await CurrentChainOwnerAsync())).Succeeded);
    }

    [Fact]
    public async Task VerifyChain_DetectsAlteredEntry()
    {
        // 这是哈希链存在的唯一理由：有库权限的人改了内容，校验必须能发现。
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "original");
        await auditor.RecordAsync("Tip", "second");

        var first = await DbContext.Set<AuditRecordAccess>()
            .OrderBy(e => e.Sequence).FirstAsync();
        first.ResourceId = "tampered";
        await DbContext.SaveChangesAsync();

        var result = await auditor.VerifyChainAsync(await CurrentChainOwnerAsync());

        Assert.False(result.Succeeded);
        Assert.Contains("altered", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyChain_DetectsDeletedEntry()
    {
        // 删掉中间一条：其后条目的 PreviousHash 就接不上了。
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "1");
        await auditor.RecordAsync("Tip", "2");
        await auditor.RecordAsync("Tip", "3");

        var middle = await DbContext.Set<AuditRecordAccess>()
            .Where(e => e.Sequence == 2).FirstAsync();
        DbContext.Set<AuditRecordAccess>().Remove(middle);
        await DbContext.SaveChangesAsync();

        var result = await auditor.VerifyChainAsync(await CurrentChainOwnerAsync());

        Assert.False(result.Succeeded);
        Assert.Contains("previous hash mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quota_RefusesReadsBeyondTheHourlyLimit()
    {
        // 批量导出防线：2026 年 3 月那次同业泄露中，没有任何一层察觉到
        // 「这个账号今天读的量是平时的一万倍」。
        var auditor = CreateAuditor(Enabled(quota: 3));

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await auditor.RecordAsync("Tip", $"tip-{i}")).Succeeded);
        }

        var refused = await auditor.RecordAsync("Tip", "tip-4");

        Assert.False(refused.Succeeded);
        Assert.Equal(429, refused.Code);
        Assert.Equal(3, await DbContext.Set<AuditRecordAccess>().CountAsync());
    }

    [Fact]
    public async Task Quota_ZeroMeansUnlimited()
    {
        var auditor = CreateAuditor(Enabled(quota: 0));

        for (var i = 0; i < 10; i++)
        {
            Assert.True((await auditor.RecordAsync("Tip", $"tip-{i}")).Succeeded);
        }

        Assert.Equal(10, await DbContext.Set<AuditRecordAccess>().CountAsync());
    }

    [Fact]
    public async Task Quota_OnlyCountsTheLastHour()
    {
        // 配额是滑动窗口，不是累计总量：昨天读过的不该占今天的额度。
        var auditor = CreateAuditor(Enabled(quota: 2));
        await auditor.RecordAsync("Tip", "old-1");

        var stale = await DbContext.Set<AuditRecordAccess>().FirstAsync();
        stale.CreationTime = DateTime.UtcNow.AddHours(-2);
        await DbContext.SaveChangesAsync();

        Assert.True((await auditor.RecordAsync("Tip", "new-1")).Succeeded);
        Assert.True((await auditor.RecordAsync("Tip", "new-2")).Succeeded);
        Assert.False((await auditor.RecordAsync("Tip", "new-3")).Succeeded);
    }

    // ---- 查询（这才是这项能力存在的理由：登记是手段，查得出是目的） ----

    [Fact]
    public async Task Query_ByResource_AnswersWhoReadThisRecord()
    {
        // 「上个月谁看过这位举报人的材料」——本能力的招牌问题。
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "tip-42", "case-review");
        await auditor.RecordAsync("Tip", "tip-99", "case-review");
        await auditor.RecordAsync("Case", "tip-42", "case-review");

        var result = await auditor.GetAccessesAsync(
            new RecordAccessQueryDto { ResourceType = "Tip", ResourceId = "tip-42" });

        Assert.True(result.Succeeded);
        var entry = Assert.Single(result.Data!.Items);
        Assert.Equal("Tip", entry.ResourceType);
        Assert.Equal("tip-42", entry.ResourceId);
        Assert.Equal(TestHelper.DefaultTestUserName, entry.UserName);
    }

    [Fact]
    public async Task Query_ByUser_AnswersWhatThisPersonRead()
    {
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "1");
        await auditor.RecordAsync("Tip", "2");

        var owner = await CurrentChainOwnerAsync();
        var result = await auditor.GetAccessesAsync(new RecordAccessQueryDto { UserId = owner });

        Assert.Equal(2, result.Data!.TotalCount);
    }

    [Fact]
    public async Task Query_FiltersByPurposeAndTimeRange()
    {
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "1", "export");
        await auditor.RecordAsync("Tip", "2", "case-review");

        var byPurpose = await auditor.GetAccessesAsync(new RecordAccessQueryDto { Purpose = "export" });
        Assert.Equal("1", Assert.Single(byPurpose.Data!.Items).ResourceId);

        // 时间窗完全落在过去，应当一条都不返回。
        var past = await auditor.GetAccessesAsync(new RecordAccessQueryDto
        {
            EndTime = DateTime.UtcNow.AddHours(-1)
        });
        Assert.Equal(0, past.Data!.TotalCount);
    }

    [Fact]
    public async Task Query_ReturnsNewestFirst()
    {
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "first");
        await auditor.RecordAsync("Tip", "second");

        var result = await auditor.GetAccessesAsync(new RecordAccessQueryDto());

        Assert.Equal("second", result.Data!.Items.First().ResourceId);
    }

    [Fact]
    public async Task Query_WhenDisabled_ReturnsEmptyPageInsteadOfFailing()
    {
        // 未启用时表都不存在；返回空页让调用方不必判断开关。
        var auditor = CreateAuditor(new RecordAccessAuditOptions { Enabled = false });

        var result = await auditor.GetAccessesAsync(new RecordAccessQueryDto());

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data!.TotalCount);
    }

    [Fact]
    public async Task UserStatistics_SurfaceReadVolumePerUser()
    {
        // 配额是事前闸门，统计是事后视角：没超配额但读得异常多的账号只有这样才看得出来。
        var auditor = CreateAuditor(Enabled());
        await auditor.RecordAsync("Tip", "1");
        await auditor.RecordAsync("Tip", "2");
        await auditor.RecordAsync("Tip", "1");   // 同一条记录读两次

        var result = await auditor.GetUserStatisticsAsync();

        Assert.True(result.Succeeded);
        var stat = Assert.Single(result.Data!);
        Assert.Equal(3, stat.AccessCount);
        Assert.Equal(2, stat.DistinctRecordCount);   // 去重后只有两条不同的记录
        Assert.Equal(TestHelper.DefaultTestUserName, stat.UserName);
    }

    [Fact]
    public async Task UserStatistics_RejectsNonPositiveTopN()
    {
        var auditor = CreateAuditor(Enabled());

        var result = await auditor.GetUserStatisticsAsync(topN: 0);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }
}
