using System.Linq.Expressions;
using Tnzi.MultiTenancy;
using System.Text;
using System.Text.Json;
using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 策略驱动数据销毁：到期判定、诉讼保全、硬删除、销毁证明与哈希链。
/// </summary>
public class DataDestructionServiceTests : IntegrationTestBase
{
    private const string PolicyName = "test-retention";

    // ---- 测试替身 -----------------------------------------------------------

    private sealed class StubPolicyProvider(params RetentionPolicy[] policies) : IRetentionPolicyProvider
    {
        public IEnumerable<RetentionPolicy> GetPolicies() => policies;
    }

    private sealed class StubHoldProvider(params string[] heldIds) : ILitigationHoldProvider
    {
        public Task<IReadOnlyCollection<string>> GetHeldIdentifiersAsync(
            string policyName, Type entityType, IReadOnlyCollection<string> candidates, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(
                candidates.Where(heldIds.Contains).ToList());
    }

    private sealed class ThrowingHoldProvider : ILitigationHoldProvider
    {
        public Task<IReadOnlyCollection<string>> GetHeldIdentifiersAsync(
            string policyName, Type entityType, IReadOnlyCollection<string> candidates, CancellationToken ct = default)
            => throw new InvalidOperationException("hold system unreachable");
    }

    /// <summary>把被试实体指向测试 DbContext（生产里由 EntityManager 的注册表回答）。</summary>
    private sealed class StubEntityManager : IEntityManager
    {
        public void Initialize() { }
        public IEntityRegister[] GetEntityRegisters(Type dbContextType) => [];
        public Type GetDbContextTypeForEntity(Type entityType) => typeof(AuditTestDbContext);
        public Type[] GetAllEntityTypes() => [typeof(RetentionTestRecord)];
        public Type[] GetAllDbContextTypes() => [typeof(AuditTestDbContext)];
    }

    // ---- 构造助手 -----------------------------------------------------------

    private static DataDestructionOptions Enabled(bool dryRun = false, int batchSize = 500, bool storeIds = false)
        => new() { Enabled = true, DryRun = dryRun, BatchSize = batchSize, StoreIdentifiers = storeIds };

    private static RetentionPolicy<RetentionTestRecord> Policy(
        TimeSpan? period = null,
        string? keyId = null,
        string name = PolicyName,
        Expression<Func<RetentionTestRecord, bool>>? scope = null)
        => new()
        {
            Name = name,
            RetentionPeriod = period ?? TimeSpan.FromDays(30),
            Timestamp = r => r.CreationTime,
            Scope = scope,
            EncryptionKeyId = keyId
        };

    private DataDestructionService CreateService(
        DataDestructionOptions options,
        IEnumerable<RetentionPolicy>? policies = null,
        IEnumerable<ILitigationHoldProvider>? holds = null,
        IDataDestroyer? destroyer = null,
        FieldEncryptionOptions? encryption = null,
        MultiTenancyOptions? multiTenancy = null)
    {
        var repository = new EFCoreRepository<AuditTestDbContext, AuditDataDestruction, Guid>(
            DbContext, serviceProvider: ServiceProvider);

        return new DataDestructionService(
            ServiceProvider,
            repository,
            new StaticOptionsMonitor<DataDestructionOptions>(options),
            [new StubPolicyProvider((policies ?? [Policy()]).ToArray())],
            holds ?? [],
            destroyer ?? new HardDeleteDataDestroyer(ServiceProvider, new StubEntityManager()),
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            encryption == null ? null : new StaticOptionsMonitor<FieldEncryptionOptions>(encryption),
            multiTenancy == null ? null : Microsoft.Extensions.Options.Options.Create(multiTenancy));
    }

    private async Task<RetentionTestRecord> SeedAsync(
        int ageInDays, string category = "general", bool softDeleted = false)
    {
        var record = new RetentionTestRecord
        {
            Id = Guid.NewGuid(),
            Category = category,
            CreationTime = DateTime.UtcNow.AddDays(-ageInDays),
            ClosedAt = DateTime.UtcNow.AddDays(-ageInDays),
            IsDeleted = softDeleted
        };

        DbContext.Set<RetentionTestRecord>().Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }

    private async Task<List<RetentionTestRecord>> SurvivingRecordsAsync()
        => await DbContext.Set<RetentionTestRecord>().IgnoreQueryFilters().ToListAsync();

    private async Task<List<AuditDataDestruction>> CertificatesAsync()
        => await DbContext.Set<AuditDataDestruction>().OrderBy(e => e.Sequence).ToListAsync();

    // ---- 可选性 -------------------------------------------------------------

    [Fact]
    public async Task Disabled_DestroysNothingAndWritesNoCertificate()
    {
        // 「可选能力」的核心断言：不启用就一条数据都不动。
        await SeedAsync(ageInDays: 100);
        var service = CreateService(new DataDestructionOptions { Enabled = false });

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Single(await SurvivingRecordsAsync());
        Assert.Empty(await CertificatesAsync());
    }

    [Fact]
    public async Task NoPolicies_SucceedsWithoutTouchingData()
    {
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(), policies: []);

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!.Policies);
        Assert.Single(await SurvivingRecordsAsync());
    }

    // ---- 到期判定 -----------------------------------------------------------

    [Fact]
    public async Task OnlyExpiredRecordsAreDestroyed()
    {
        var expired = await SeedAsync(ageInDays: 100);
        var fresh = await SeedAsync(ageInDays: 5);
        var service = CreateService(Enabled(), [Policy(TimeSpan.FromDays(30))]);

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalDestroyed);

        var survivors = await SurvivingRecordsAsync();
        Assert.Equal(fresh.Id, Assert.Single(survivors).Id);
        Assert.DoesNotContain(survivors, r => r.Id == expired.Id);
    }

    [Fact]
    public async Task NothingExpired_WritesNoCertificate()
    {
        // 证明链是销毁发生过的证据，不是心跳——每天一条「销毁了 0 条」会淹掉真正的记录。
        await SeedAsync(ageInDays: 5);
        var service = CreateService(Enabled(), [Policy(TimeSpan.FromDays(30))]);

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(await CertificatesAsync());
        Assert.Null(Assert.Single(result.Data!.Policies).CertificateId);
    }

    [Fact]
    public async Task ScopeRestrictsThePolicyToMatchingRecords()
    {
        var inScope = await SeedAsync(ageInDays: 100, category: "closed");
        var outOfScope = await SeedAsync(ageInDays: 100, category: "open");
        var service = CreateService(
            Enabled(),
            [Policy(TimeSpan.FromDays(30), scope: r => r.Category == "closed")]);

        await service.RunAsync();

        var survivors = await SurvivingRecordsAsync();
        Assert.Equal(outOfScope.Id, Assert.Single(survivors).Id);
        Assert.DoesNotContain(survivors, r => r.Id == inScope.Id);
    }

    // ---- 销毁必须是真删 -----------------------------------------------------

    [Fact]
    public async Task Destruction_IsAHardDelete_NotASoftDelete()
    {
        // ★这条是整套机制最容易做错的地方：仓储的 DeleteAsync 对软删实体只会把 IsDeleted 置真，
        //   而那样数据仍在库里、也仍在每一份备份里，合规意义上根本没有销毁。
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled());

        await service.RunAsync();

        // IgnoreQueryFilters：绕开软删过滤器去看真实的行是否还在。
        Assert.Empty(await SurvivingRecordsAsync());
    }

    [Fact]
    public async Task AlreadySoftDeletedRecords_AreAlsoDestroyed()
    {
        // 已软删的行同样占着库、同样在备份里，保留期对它们一视同仁。
        await SeedAsync(ageInDays: 100, softDeleted: true);
        var service = CreateService(Enabled());

        var result = await service.RunAsync();

        Assert.Equal(1, result.Data!.TotalDestroyed);
        Assert.Empty(await SurvivingRecordsAsync());
    }

    // ---- 诉讼保全 -----------------------------------------------------------

    [Fact]
    public async Task LitigationHold_PreventsDestructionAndIsCounted()
    {
        var held = await SeedAsync(ageInDays: 100);
        var free = await SeedAsync(ageInDays: 100);
        var service = CreateService(
            Enabled(), holds: [new StubHoldProvider(held.Id.ToString())]);

        var result = await service.RunAsync();

        Assert.Equal(1, result.Data!.TotalDestroyed);
        Assert.Equal(1, result.Data.TotalHeld);

        var survivors = await SurvivingRecordsAsync();
        Assert.Equal(held.Id, Assert.Single(survivors).Id);
        Assert.DoesNotContain(survivors, r => r.Id == free.Id);

        // 证明必须写明「有多少条到期却没销毁」，否则读的人分不清是没到期还是漏销毁。
        var certificate = Assert.Single(await CertificatesAsync());
        Assert.Equal(1, certificate.DestroyedCount);
        Assert.Equal(1, certificate.HeldCount);
    }

    [Fact]
    public async Task EverythingHeld_StillWritesACertificate()
    {
        // 整批被保全时反而更需要证明：否则「到期了却一条没销毁」看起来像漏跑。
        var held = await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(), holds: [new StubHoldProvider(held.Id.ToString())]);

        var result = await service.RunAsync();

        Assert.Equal(0, result.Data!.TotalDestroyed);
        Assert.Equal(1, result.Data.TotalHeld);
        Assert.Single(await SurvivingRecordsAsync());

        var certificate = Assert.Single(await CertificatesAsync());
        Assert.Equal(0, certificate.DestroyedCount);
        Assert.Equal(1, certificate.HeldCount);
    }

    [Fact]
    public async Task HoldProviderFailure_AbortsThePolicyWithoutDestroying()
    {
        // 保全系统查不通时宁可不销毁：晚一天只是延迟，销毁了不该销毁的无法撤销。
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(), holds: [new ThrowingHoldProvider()]);

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        var policy = Assert.Single(result.Data!.Policies);
        Assert.NotNull(policy.Error);
        Assert.Equal(0, policy.DestroyedCount);
        Assert.Single(await SurvivingRecordsAsync());
        Assert.Empty(await CertificatesAsync());
    }

    // ---- 空跑 ---------------------------------------------------------------

    [Fact]
    public async Task DryRun_ReportsAndCertifiesButDestroysNothing()
    {
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(dryRun: true));

        var result = await service.RunAsync();

        Assert.True(result.Data!.IsDryRun);
        Assert.Equal(1, result.Data.TotalDestroyed);   // 报告「会删掉多少」
        Assert.Single(await SurvivingRecordsAsync());  // 但数据还在

        var certificate = Assert.Single(await CertificatesAsync());
        Assert.True(certificate.IsDryRun);
        Assert.Contains("dry-run", certificate.Mode);
    }

    // ---- 批量上限 -----------------------------------------------------------

    [Fact]
    public async Task BatchSize_CapsOneCycleAndFlagsHasMore()
    {
        await SeedAsync(ageInDays: 100);
        await SeedAsync(ageInDays: 100);
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(batchSize: 2));

        var result = await service.RunAsync();

        Assert.Equal(2, result.Data!.TotalDestroyed);
        Assert.True(Assert.Single(result.Data.Policies).HasMore);
        Assert.Single(await SurvivingRecordsAsync());
    }

    // ---- 销毁证明 -----------------------------------------------------------

    [Fact]
    public async Task Certificate_RecordsPolicyEntityAndDigestButNotTheData()
    {
        var record = await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled());

        await service.RunAsync();

        var certificate = Assert.Single(await CertificatesAsync());
        Assert.Equal(PolicyName, certificate.PolicyName);
        Assert.Equal(typeof(RetentionTestRecord).FullName, certificate.EntityType);
        Assert.Equal(1, certificate.Sequence);
        Assert.Equal("hard-delete", certificate.Mode);
        Assert.NotEmpty(certificate.IdentifierDigest);
        Assert.NotEmpty(certificate.Hash);
        Assert.Equal(string.Empty, certificate.PreviousHash);

        // 默认不留「曾经存在过哪些记录」的清单——那本身就是元数据泄漏。
        Assert.Null(certificate.Identifiers);
        Assert.DoesNotContain(record.Id.ToString(), certificate.IdentifierDigest);
    }

    [Fact]
    public async Task StoreIdentifiers_KeepsTheListWhenExplicitlyEnabled()
    {
        var record = await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(storeIds: true));

        await service.RunAsync();

        var certificate = Assert.Single(await CertificatesAsync());
        Assert.NotNull(certificate.Identifiers);
        Assert.Contains(record.Id.ToString(), certificate.Identifiers);
    }

    [Fact]
    public async Task IdentifierDigest_IsIndependentOfOrder()
    {
        // 摘要排序后再算，持有原始清单的人才能复算验证。
        await SeedAsync(ageInDays: 100);
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(storeIds: true));

        await service.RunAsync();

        var certificate = Assert.Single(await CertificatesAsync());
        var ids = JsonSerializer.Deserialize<List<string>>(certificate.Identifiers!)!;

        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join('\u001F', ids.OrderBy(i => i, StringComparer.Ordinal)))));

        Assert.Equal(expected, certificate.IdentifierDigest);
    }

    // ---- 哈希链 -------------------------------------------------------------

    [Fact]
    public async Task ConsecutiveRuns_FormAHashChain()
    {
        var service = CreateService(Enabled());

        await SeedAsync(ageInDays: 100);
        await service.RunAsync();
        await SeedAsync(ageInDays: 100);
        await service.RunAsync();

        var certificates = await CertificatesAsync();
        Assert.Equal(2, certificates.Count);
        Assert.Equal([1L, 2L], certificates.Select(c => c.Sequence));
        Assert.Equal(string.Empty, certificates[0].PreviousHash);
        Assert.Equal(certificates[0].Hash, certificates[1].PreviousHash);

        Assert.True((await service.VerifyChainAsync()).Succeeded);
    }

    [Fact]
    public async Task VerifyChain_DetectsAnAlteredCertificate()
    {
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled());
        await service.RunAsync();

        // 事后把「销毁了多少条」改小——这正是链要拦下的动作。
        var certificate = Assert.Single(await CertificatesAsync());
        certificate.DestroyedCount = 0;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var verify = await service.VerifyChainAsync();

        Assert.False(verify.Succeeded);
        Assert.Contains("altered", verify.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyChain_DetectsADeletedCertificate()
    {
        var service = CreateService(Enabled());

        await SeedAsync(ageInDays: 100);
        await service.RunAsync();
        await SeedAsync(ageInDays: 100);
        await service.RunAsync();

        // 抽掉第一条：其后所有条目的 PreviousHash 都对不上了。
        var first = (await CertificatesAsync())[0];
        DbContext.Set<AuditDataDestruction>().Remove(first);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var verify = await service.VerifyChainAsync();

        Assert.False(verify.Succeeded);
        Assert.Contains("previous hash", verify.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- 加密密钥回查 -------------------------------------------------------

    [Fact]
    public async Task KeyStillInRing_IsReportedAsNotDestroyed()
    {
        await SeedAsync(ageInDays: 100);
        var service = CreateService(
            Enabled(),
            [Policy(keyId: "tip-2026")],
            encryption: new FieldEncryptionOptions
            {
                Enabled = true,
                ActiveKeyId = "tip-2026",
                Keys = { ["tip-2026"] = Convert.ToBase64String(new byte[32]) }
            });

        await service.RunAsync();

        var certificate = Assert.Single(await CertificatesAsync());
        Assert.Equal("tip-2026", certificate.EncryptionKeyId);
        Assert.False(certificate.IsKeyDestroyed);
    }

    [Fact]
    public async Task KeyRemovedFromRing_IsReportedAsDestroyed()
    {
        await SeedAsync(ageInDays: 100);
        var service = CreateService(
            Enabled(),
            [Policy(keyId: "tip-2025")],
            encryption: new FieldEncryptionOptions
            {
                Enabled = true,
                ActiveKeyId = "tip-2026",
                Keys = { ["tip-2026"] = Convert.ToBase64String(new byte[32]) }
            });

        await service.RunAsync();

        Assert.True(Assert.Single(await CertificatesAsync()).IsKeyDestroyed);
    }

    [Fact]
    public async Task FieldEncryptionDisabled_NeverClaimsTheKeyWasDestroyed()
    {
        // ★密钥环此时本来就是空的。若据此判定「已销毁」，等于给每一份证明
        //   都盖上一个它没有资格盖的章。
        await SeedAsync(ageInDays: 100);
        var service = CreateService(
            Enabled(),
            [Policy(keyId: "tip-2025")],
            encryption: new FieldEncryptionOptions { Enabled = false });

        await service.RunAsync();

        Assert.False(Assert.Single(await CertificatesAsync()).IsKeyDestroyed);
    }

    // ---- 多策略隔离 ---------------------------------------------------------

    [Fact]
    public async Task DuplicatePolicyNames_AreRejected()
    {
        // 同名策略会让证明链上的记录无法归属到确定的一条策略。
        var service = CreateService(Enabled(), [Policy(name: "dup"), Policy(name: "dup")]);

        var result = await service.RunAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
    }

    [Fact]
    public async Task OneFailingPolicy_DoesNotBlockTheOthers()
    {
        await SeedAsync(ageInDays: 100);

        // 第一条策略指向一个没有仓储注册的实体类型，必然失败。
        var broken = new RetentionPolicy<AuditOperation>
        {
            Name = "broken",
            RetentionPeriod = TimeSpan.FromDays(1),
            Timestamp = o => o.CreationTime
        };

        var service = CreateService(Enabled(), [broken, Policy(name: "healthy")]);

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Policies.Count);
        Assert.NotNull(result.Data.Policies.Single(p => p.PolicyName == "broken").Error);

        // 健康的那条照常销毁。
        Assert.Equal(1, result.Data.Policies.Single(p => p.PolicyName == "healthy").DestroyedCount);
        Assert.Empty(await SurvivingRecordsAsync());
    }

    // ---- 证明查询 -----------------------------------------------------------

    [Fact]
    public async Task GetCertificates_ReturnsNewestFirstAndFiltersDryRuns()
    {
        var real = CreateService(Enabled());
        await SeedAsync(ageInDays: 100);
        await real.RunAsync();

        var dry = CreateService(Enabled(dryRun: true));
        await SeedAsync(ageInDays: 100);
        await dry.RunAsync();

        var all = await real.GetCertificatesAsync(new DataDestructionQueryDto());
        Assert.Equal(2, all.Data!.TotalCount);
        // 最近一次排在最前。
        Assert.Equal(2, all.Data.Items.First().Sequence);

        var realOnly = await real.GetCertificatesAsync(new DataDestructionQueryDto { IsDryRun = false });
        Assert.Equal(1, realOnly.Data!.TotalCount);
        Assert.False(realOnly.Data.Items.Single().IsDryRun);
    }

    // ---- 多租户隔离 -------------------------------------------------------

    [Fact]
    public async Task MultiTenancyDisabled_RunsOnceWithoutTenantIteration()
    {
        // 未启用多租户时整库是单一逻辑租户：不该为此多发一次 distinct 查询。
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(), multiTenancy: new MultiTenancyOptions { Enabled = false });

        var result = await service.RunAsync();

        Assert.Equal(1, result.Data!.TotalDestroyed);
        Assert.Empty(await SurvivingRecordsAsync());
    }

    [Fact]
    public async Task MultiTenancyEnabled_NonTenantEntity_StillRunsOnce()
    {
        // ★被试实体不实现 IMultiTenant：它没有租户维度，按租户迭代无从谈起，
        //   必须照常跑一次而不是被跳过（跳过 = 这类实体的到期数据永远不销毁）。
        await SeedAsync(ageInDays: 100);
        var service = CreateService(Enabled(), multiTenancy: new MultiTenancyOptions { Enabled = true });

        var result = await service.RunAsync();

        Assert.Equal(1, result.Data!.TotalDestroyed);
        Assert.Empty(await SurvivingRecordsAsync());
    }
}
