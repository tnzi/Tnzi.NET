using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// P1 多租户隔离门禁 - 证明 <see cref="McpServerRegistration"/> 是真正的 <see cref="IMultiTenant"/> 实体，
/// 其凭证承载行（AuthTokenEncrypted）在多租户开启下被 EF Core 全局查询过滤器按租户隔离。
/// <para>
/// 与"共享资源 Scope 模式"（Provider 有意 <b>不</b> 实现 IMultiTenant）相反：
/// McpServerRegistration 存储 per-registration 加密凭证，租户私有才是安全默认。它实现 IMultiTenant
/// 后，全局过滤器 <c>e.TenantId == currentTenant</c> 自动附加 → 一个租户的注册（及其凭证）
/// 对其他租户不可见，且插入时自动加盖 TenantId。
/// </para>
/// <para>
/// 唯一 Name 索引随之按租户分区（(TenantId, Name)），所以同一个 MCP server 名字可以在不同租户下各存一条。
/// </para>
/// </summary>
public class McpServerRegistrationMtIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public McpServerRegistrationMtIsolationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        // 用"无当前租户"的种子上下文建表 + 写入显式 TenantId=A 的 "srv1"：
        // 无租户上下文 → AuditPropertyHelper 不会改写显式赋的 TenantId，
        // 让我们精确造出一条属于租户 A 的注册行（带其加密凭证字段语义）。
        using var seedContext = CreateContext(currentTenantId: null);
        seedContext.Database.EnsureCreated();
        seedContext.Set<McpServerRegistration>().Add(new McpServerRegistration
        {
            Name = "srv1",
            ServerUrl = "https://mcp.example.com/sse",
            Transport = "sse",
            TenantId = _tenantA
        });
        seedContext.SaveChangesAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // =====================================================================
    // 证明 A - 租户 A 看见自己的 "srv1"
    // =====================================================================

    [Fact]
    public async Task MtEnabled_TenantA_SeesOwnRegistration()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantA);
        var service = CreateService(ctx);

        var result = await service.GetPagedListAsync(new McpServerRegistrationQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(r => r.Name).ShouldContain("srv1",
            "Tenant A's own registration MUST be visible under tenant A.");
    }

    // =====================================================================
    // 证明 B - 租户 B 看不到 A 的 "srv1"（全局过滤器隔离凭证承载行）
    // =====================================================================

    [Fact]
    public async Task MtEnabled_TenantB_DoesNotSeeTenantARegistration()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantB);
        var service = CreateService(ctx);

        var result = await service.GetPagedListAsync(new McpServerRegistrationQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(r => r.Name).ShouldNotContain("srv1",
            "Tenant A's MCP server registration (with its encrypted credential) MUST be hidden from tenant B " +
            "because McpServerRegistration is IMultiTenant and the global filter scopes it per tenant.");
    }

    // =====================================================================
    // 证明 C - 租户 B 可创建同名 "srv1"（唯一 Name 按租户分区，非全局）
    // =====================================================================

    [Fact]
    public async Task MtEnabled_TenantB_CanCreateSameNameAsTenantA()
    {
        await using var ctx = CreateContext(currentTenantId: _tenantB);
        var service = CreateService(ctx);

        var result = await service.CreateAsync(new CreateMcpServerRegistrationDto
        {
            Name = "srv1",
            ServerUrl = "https://mcp.tenant-b.com/sse",
            Transport = "sse"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Name.ShouldBe("srv1");

        // 加盖到了 B：B 现在看得到自己的 "srv1"。
        var listB = await service.GetPagedListAsync(new McpServerRegistrationQueryDto { PageIndex = 1, PageSize = 50 });
        listB.Data!.Items.Count(r => r.Name == "srv1").ShouldBe(1);

        // 而 A 仍只看到自己那条，不受 B 的创建影响。
        await using var ctxA = CreateContext(currentTenantId: _tenantA);
        var serviceA = CreateService(ctxA);
        var listA = await serviceA.GetPagedListAsync(new McpServerRegistrationQueryDto { PageIndex = 1, PageSize = 50 });
        listA.Data!.Items.Count(r => r.Name == "srv1").ShouldBe(1);
    }

    // =====================================================================
    // 工厂 + 桩
    // =====================================================================

    private McpServerRegistrationMtDbContext CreateContext(Guid? currentTenantId)
    {
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);
        currentUserMock.Setup(m => m.TenantId).Returns((Guid?)null);

        var currentTenant = new StubCurrentTenant(currentTenantId);

        var options = new DbContextOptionsBuilder<McpServerRegistrationMtDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                Tnzi.EFCore.Internal.MultiTenancyModelCacheKeyFactory>()
            .Options;

        return new McpServerRegistrationMtDbContext(
            options,
            currentUserMock.Object,
            currentTenant,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    private McpServerRegistryService CreateService(McpServerRegistrationMtDbContext ctx)
    {
        var repo = new EFCoreRepository<McpServerRegistrationMtDbContext, McpServerRegistration, Guid>(ctx);
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        var clientFactory = new Mock<Tnzi.AI.Infrastructure.Mcp.IMcpClientFactory>();
        var catalog = new Mock<Tnzi.AI.Infrastructure.Mcp.IMcpServerCatalog>();
        var toolProvider = new Mock<IMcpToolProvider>();
        var aiOptions = Mock.Of<IOptionsMonitor<AIOptions>>(m => m.CurrentValue == new AIOptions { Mcp = { AllowPrivateEndpoints = true } });
        return new McpServerRegistryService(
            repo, new StubDataProtectionProvider(), clientFactory.Object, catalog.Object, toolProvider.Object, aiOptions, sp);
    }

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

/// <summary>
/// 测试专用 DbContext - 仅注册 <see cref="McpServerRegistration"/>，通过显式传入
/// <see cref="MultiTenancyOptions"/> 控制 MT 开关。
/// </summary>
internal sealed class McpServerRegistrationMtDbContext : TnziDbContext<McpServerRegistrationMtDbContext>
{
    public McpServerRegistrationMtDbContext(
        DbContextOptions<McpServerRegistrationMtDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant,
        IOptions<MultiTenancyOptions> multiTenancyOptions)
        : base(options, currentUser, currentTenant, multiTenancyOptions: multiTenancyOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 通过 RegisterTo(modelBuilder, this) 应用配置（而非裸 ApplyConfiguration）——
        // 这条路径会把当前 DbContext 注入 EntityConfigurationContext，使配置类里的
        // GetDbContext() 能正确读到 IsMultiTenancyEnabled，从而构建 (TenantId, Name) 唯一索引，
        // 与生产环境（自动实体注册路径）保持一致。
        new McpServerRegistrationConfiguration().RegisterTo(modelBuilder, this);

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
