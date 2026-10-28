using Microsoft.EntityFrameworkCore.Infrastructure;
using Tnzi.EFCore.DocumentNumbering;
using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 多租户开启时，宿主作用域（<c>TenantId</c> 为 NULL）的单据序列每个 scope 也只能有一行。
/// </summary>
/// <remarks>
/// <para>
/// ★ <c>DocumentNumberService</c> 的并发首插只靠唯一索引兜底：两个事务同时为新 scope 插首行，后到者撞约束、转去走递增路径。
/// 多租户下那条索引是 <c>(TenantId, Scope)</c>，而唯一索引里 NULL 互不相等（SQL Server 上 EF 还会自动给它加
/// <c>TenantId IS NOT NULL</c> 过滤）：无租户上下文的后台作业并发首次取号，两行 <c>(NULL, scope)</c> 都插得进去，
/// 两张单据都拿到 1 号。
/// </para>
/// <para>
/// 真实 SQLite + 多租户开启的模型，断言落在数据库约束上而不是模型元数据上。
/// </para>
/// </remarks>
public sealed class DocumentSequenceHostScopeUniquenessTests : IDisposable
{
    private const string Scope = "finance.invoice";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<DocumentSequenceProbeDbContext> _options;

    public DocumentSequenceHostScopeUniquenessTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<DocumentSequenceProbeDbContext>()
            .UseSqlite(_connection)
            .UseTnziMultiTenancy(true)
            .Options;
        using var context = CreateContext(tenantId: null);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task HostScope_SecondRowForTheSameScope_IsRejectedByTheDatabase()
    {
        await InsertAsync(tenantId: null);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(tenantId: null));

        Assert.True(ex.IsUniqueConstraintViolation(), ex.InnerException?.Message ?? ex.Message);
    }

    /// <summary>对照组：同一 scope 在不同租户与宿主下各一行是正常形态，新索引不能误伤。</summary>
    [Fact]
    public async Task SameScope_InEachTenantAndTheHost_IsAllowed()
    {
        await InsertAsync(Guid.NewGuid());
        await InsertAsync(Guid.NewGuid());
        await InsertAsync(tenantId: null);

        await using var context = CreateContext(tenantId: null);
        Assert.Equal(3, await context.Set<DocumentSequence>().IgnoreQueryFilters().CountAsync(s => s.Scope == Scope));
    }

    [Fact]
    public async Task TenantScope_SecondRowForTheSameScope_IsStillRejected()
    {
        var tenantId = Guid.NewGuid();
        await InsertAsync(tenantId);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(tenantId));

        Assert.True(ex.IsUniqueConstraintViolation(), ex.InnerException?.Message ?? ex.Message);
    }

    /// <summary>
    /// MySQL 没有部分索引：Pomelo 生成索引时丢掉过滤条件，宿主索引落到库里就是 <c>Scope</c> 上的全局唯一索引，
    /// 第二个租户为同一 scope 首次取号就撞约束、重试耗尽后失败。MySQL 上不能建这条索引。
    /// </summary>
    [Fact]
    public void MySqlModel_DoesNotDeclareTheHostScopeIndex()
    {
        var options = new DbContextOptionsBuilder<DocumentSequenceMySqlProbeDbContext>();
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new FakeMySqlProviderMarkerExtension());
        options.UseSqlite(_connection).UseTnziMultiTenancy(true);

        using var context = new DocumentSequenceMySqlProbeDbContext(options.Options, new MockCurrentUser(), new MockCurrentTenant());
        var indexes = context.Model.FindEntityType(typeof(DocumentSequence))!.GetIndexes().ToList();

        Assert.DoesNotContain(indexes, i => i.GetDatabaseName() == "IX_DocumentSequence_Host_Scope");
        Assert.Contains(indexes, i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "Scope"]));
    }

    private async Task InsertAsync(Guid? tenantId)
    {
        await using var context = CreateContext(tenantId);
        context.Set<DocumentSequence>().Add(new DocumentSequence { Scope = Scope, NextValue = 2 });
        await context.SaveChangesAsync();
    }

    private DocumentSequenceProbeDbContext CreateContext(Guid? tenantId)
    {
        var tenant = new MockCurrentTenant();
        if (tenantId.HasValue)
        {
            tenant.SetTenant(tenantId.Value, "tenant");
        }

        return new DocumentSequenceProbeDbContext(_options, new MockCurrentUser(), tenant);
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>只装单据序列一个实体的上下文：经框架的实体注册入口套用它的配置（多租户开关在那里读到）。</summary>
public class DocumentSequenceProbeDbContext : TnziDbContext<DocumentSequenceProbeDbContext>
{
    public DocumentSequenceProbeDbContext(DbContextOptions<DocumentSequenceProbeDbContext> options, ICurrentUser currentUser, ICurrentTenant? currentTenant = null)
        : base(options, currentUser, currentTenant)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new DocumentSequenceConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>同上，单独一个类型：模型缓存按上下文类型分，不能与 SQLite 那份共用。</summary>
public class DocumentSequenceMySqlProbeDbContext : TnziDbContext<DocumentSequenceMySqlProbeDbContext>
{
    public DocumentSequenceMySqlProbeDbContext(DbContextOptions<DocumentSequenceMySqlProbeDbContext> options, ICurrentUser currentUser, ICurrentTenant? currentTenant = null)
        : base(options, currentUser, currentTenant)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new DocumentSequenceConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// 让实体配置期的 provider 识别把上下文认成 MySQL：识别按 options 扩展的类型名，排在 SQLite 扩展之前即先命中。
/// 测试只读模型元数据，不连库。
/// </summary>
public sealed class FakeMySqlProviderMarkerExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => string.Empty;

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
        }
    }
}
