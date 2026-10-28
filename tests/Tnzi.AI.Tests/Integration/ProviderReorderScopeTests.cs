using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// Provider 重排的范围（真实 SQLite + 多租户开启）。Provider 刻意不是 IMultiTenant，没有全局租户过滤：
/// 重排不给范围时整张表是一条序列，租户管理员提交 System 行的 id 就能改写所有租户看到的展示顺序。
/// </summary>
public class ProviderReorderScopeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _system1 = Guid.NewGuid();
    private readonly Guid _system2 = Guid.NewGuid();
    private readonly Guid _tenantA1 = Guid.NewGuid();
    private readonly Guid _tenantA2 = Guid.NewGuid();
    private readonly Guid _tenantB1 = Guid.NewGuid();

    public ProviderReorderScopeTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        using var seed = CreateContext(currentTenantId: null);
        seed.Database.EnsureCreated();
        seed.Set<Provider>().AddRange(
            new Provider { Id = _system1, Name = "system-1", ProviderType = "OpenAI", Scope = ResourceScope.System, SortOrder = 10 },
            new Provider { Id = _tenantA1, Name = "tenant-a-1", ProviderType = "OpenAI", Scope = ResourceScope.Tenant, TenantId = _tenantA, SortOrder = 20 },
            new Provider { Id = _system2, Name = "system-2", ProviderType = "OpenAI", Scope = ResourceScope.System, SortOrder = 30 },
            new Provider { Id = _tenantA2, Name = "tenant-a-2", ProviderType = "OpenAI", Scope = ResourceScope.Tenant, TenantId = _tenantA, SortOrder = 40 },
            new Provider { Id = _tenantB1, Name = "tenant-b-1", ProviderType = "OpenAI", Scope = ResourceScope.Tenant, TenantId = _tenantB, SortOrder = 50 });
        seed.SaveChangesAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TenantCaller_SubmittingTheVisibleList_ReordersOnlyItsOwnRows()
    {
        // 列表页提交的是可见的整页：System ∪ 本租户
        var result = await ReorderAsTenantAsync(_tenantA, [_system2, _tenantA2, _system1, _tenantA1]);

        result.Succeeded.ShouldBeTrue(result.Message);
        var orders = await ReadSortOrdersAsync();
        orders[_system1].ShouldBe(10, "system rows are shared by every tenant; a tenant admin must not move them");
        orders[_system2].ShouldBe(30);
        orders[_tenantB1].ShouldBe(50, "another tenant's row is outside the caller's sequence");
        // 本租户的行是它自己的一条序列：按提交顺序排
        orders[_tenantA2].ShouldBeLessThan(orders[_tenantA1]);
    }

    [Fact]
    public async Task TenantCaller_SubmittingOnlySystemRows_ChangesNothing()
    {
        var result = await ReorderAsTenantAsync(_tenantA, [_system2, _system1]);

        result.Succeeded.ShouldBeTrue(result.Message);
        var orders = await ReadSortOrdersAsync();
        orders[_system1].ShouldBe(10);
        orders[_system2].ShouldBe(30);
    }

    [Fact]
    public async Task TenantCaller_SubmittingAnotherTenantsRow_IsRejected()
    {
        var result = await ReorderAsTenantAsync(_tenantA, [_tenantB1, _tenantA1]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await ReadSortOrdersAsync())[_tenantB1].ShouldBe(50);
    }

    [Fact]
    public async Task HostCaller_ReordersAcrossTheWholeTable()
    {
        await using var ctx = CreateContext(currentTenantId: null);
        var service = CreateService(ctx, tenantId: null);

        var result = await service.ReorderAsync([_system2, _system1]);

        result.Succeeded.ShouldBeTrue(result.Message);
        var orders = await ReadSortOrdersAsync();
        orders[_system2].ShouldBeLessThan(orders[_system1]);
    }

    private async Task<Result> ReorderAsTenantAsync(Guid tenantId, IReadOnlyList<Guid> ids)
    {
        await using var ctx = CreateContext(tenantId);
        return await CreateService(ctx, tenantId).ReorderAsync(ids);
    }

    private async Task<Dictionary<Guid, int>> ReadSortOrdersAsync()
    {
        await using var ctx = CreateContext(currentTenantId: null);
        return await ctx.Set<Provider>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.SortOrder);
    }

    private SharedResourceMtDbContext CreateContext(Guid? currentTenantId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(Guid.Empty);
        currentUser.Setup(m => m.IsAuthenticated).Returns(false);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<SharedResourceMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new SharedResourceMtDbContext(
            options,
            currentUser.Object,
            new StubCurrentTenant(currentTenantId),
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    private static ProviderService CreateService(SharedResourceMtDbContext ctx, Guid? tenantId)
        => new(
            new EFCoreRepository<SharedResourceMtDbContext, Provider, Guid>(ctx),
            new StubDataProtectionProvider(),
            Mock.Of<IHttpClientFactory>(),
            new ServiceCollection().AddLogging().BuildServiceProvider(),
            new StubCurrentTenant(tenantId));

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    private sealed class StubDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new StubProtector();
        private sealed class StubProtector : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => this;
            public byte[] Protect(byte[] plaintext) => plaintext;
            public byte[] Unprotect(byte[] protectedData) => protectedData;
        }
    }
}
