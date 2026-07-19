using AuditEntityState = Tnzi.Audit.Metadata.EntityState;

namespace Tnzi.Audit.Tests.Interceptors;

/// <summary>
/// 实体级审计拦截器测试实体（含敏感字段与超长字段场景）
/// </summary>
public class EntityAuditTestProduct : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>命中 AuditOptions.SensitiveFields 的敏感字段（大小写不敏感匹配 "password"）</summary>
    public string? Password { get; set; }

    /// <summary>无长度上限字段，用于验证采集侧截断</summary>
    public string? Description { get; set; }

    /// <summary>属性级 [AuditIgnore]：值绝不进审计行（AuthToken.Value 场景）</summary>
    [AuditIgnore]
    public string? ClientSecret { get; set; }
}

/// <summary>类级 [AuditIgnore]：整个实体类型豁免采集</summary>
[AuditIgnore]
public class AuditIgnoredVault : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 实体级审计拦截器测试 DbContext — 真实 TnziDbContext + SQLite + 拦截器挂载
/// </summary>
public class EntityAuditTestDbContext : TnziDbContext<EntityAuditTestDbContext>
{
    public EntityAuditTestDbContext(DbContextOptions<EntityAuditTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<EntityAuditTestProduct> Products => Set<EntityAuditTestProduct>();
    public DbSet<AuditOperation> AuditOperations => Set<AuditOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityAuditTestProduct>(entity =>
        {
            entity.ToTable("TestProducts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            // 唯一索引用于制造 SaveChanges 失败（验证失败时快照被丢弃）
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<AuditIgnoredVault>(entity =>
        {
            entity.ToTable("TestVaults");
            entity.HasKey(e => e.Id);
        });

        // 注册审计实体，验证自审计实体被排除（防递归）
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditOperationConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditEntityEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.Audit.Entities.Configs.AuditPropertyEntryConfiguration());

        base.OnModelCreating(modelBuilder);

        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// EntityAuditSaveChangesInterceptor 集成测试 — 真实 SQLite + TnziDbContext 保存管线
/// </summary>
public class EntityAuditSaveChangesInterceptorTests : IDisposable
{
    private sealed class StaticOptionsMonitor(AuditOptions value) : IOptionsMonitor<AuditOptions>
    {
        public AuditOptions CurrentValue => value;
        public AuditOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AuditOptions, string?> listener) => null;
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private readonly SqliteConnection _connection;
    private readonly AuditOptions _options = new();
    private readonly TestHttpContextAccessor _accessor = new() { HttpContext = new DefaultHttpContext() };
    private readonly EntityAuditCollector _collector = new();
    private readonly EntityAuditTestDbContext _context;

    public EntityAuditSaveChangesInterceptorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var interceptor = new EntityAuditSaveChangesInterceptor(
            _collector,
            new StaticOptionsMonitor(_options),
            _accessor,
            NullLogger<EntityAuditSaveChangesInterceptor>.Instance);

        var currentUser = new Mock<ICurrentUser>();
        var options = new DbContextOptionsBuilder<EntityAuditTestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .EnableSensitiveDataLogging()
            .Options;

        _context = new EntityAuditTestDbContext(options, currentUser.Object);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<EntityAuditTestProduct> SeedProductAsync(string name = "Widget", string? password = null)
    {
        var product = new EntityAuditTestProduct { Name = name, Price = 10m, Password = password };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        _collector.Drain(); // 清掉 Added 采集，聚焦后续断言
        return product;
    }

    [Fact]
    public async Task Added_CapturesNewValues_AndFinalizesEntityId()
    {
        var product = new EntityAuditTestProduct { Name = "Widget", Price = 12.5m };
        _context.Products.Add(product);

        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        entry.OperationType.ShouldBe(AuditEntityState.Added);
        entry.EntityTypeName.ShouldBe(nameof(EntityAuditTestProduct));
        entry.EntityTypeFullName.ShouldBe(typeof(EntityAuditTestProduct).FullName);
        // Added 实体的 EntityId 在 SavedChanges 定稿（框架生成的 Guid 主键）
        entry.EntityId.ShouldBe(product.Id.ToString());
        product.Id.ShouldNotBe(Guid.Empty);

        // 主键不进属性列表；非空新值逐属性记录，OriginalValue 为空
        entry.PropertyEntries.ShouldNotContain(p => p.PropertyName == nameof(EntityAuditTestProduct.Id));
        var nameProperty = entry.PropertyEntries.Single(p => p.PropertyName == nameof(EntityAuditTestProduct.Name));
        nameProperty.NewValue.ShouldBe("Widget");
        nameProperty.OriginalValue.ShouldBeNull();
        var priceProperty = entry.PropertyEntries.Single(p => p.PropertyName == nameof(EntityAuditTestProduct.Price));
        priceProperty.NewValue.ShouldBe("12.5");
    }

    [Fact]
    public async Task Modified_CapturesOnlyChangedProperties_WithOldAndNewValues()
    {
        var product = await SeedProductAsync();

        product.Name = "Renamed";
        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        entry.OperationType.ShouldBe(AuditEntityState.Modified);
        entry.EntityId.ShouldBe(product.Id.ToString());

        var property = entry.PropertyEntries.ShouldHaveSingleItem();
        property.PropertyName.ShouldBe(nameof(EntityAuditTestProduct.Name));
        property.PropertyTypeName.ShouldBe("String");
        property.OriginalValue.ShouldBe("Widget");
        property.NewValue.ShouldBe("Renamed");
    }

    [Fact]
    public async Task Deleted_CapturesOriginalValues()
    {
        var product = await SeedProductAsync();

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        entry.OperationType.ShouldBe(AuditEntityState.Deleted);
        entry.EntityId.ShouldBe(product.Id.ToString());
        var nameProperty = entry.PropertyEntries.Single(p => p.PropertyName == nameof(EntityAuditTestProduct.Name));
        nameProperty.OriginalValue.ShouldBe("Widget");
        nameProperty.NewValue.ShouldBeNull();
    }

    [Fact]
    public async Task SensitiveProperty_IsRedacted_WithSameMaskAsRequestBodyRedactor()
    {
        var product = await SeedProductAsync(password: "hunter2");

        product.Password = "changed-secret";
        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        var property = entry.PropertyEntries.Single(p => p.PropertyName == nameof(EntityAuditTestProduct.Password));
        property.OriginalValue.ShouldBe(RequestBodyRedactor.RedactedValue);
        property.NewValue.ShouldBe(RequestBodyRedactor.RedactedValue);
    }

    [Fact]
    public async Task EnableEntityAudit_False_CollectsNothing()
    {
        var product = await SeedProductAsync();
        _options.EnableEntityAudit = false;

        product.Name = "Renamed";
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task NoHttpContext_CollectsNothing()
    {
        var product = await SeedProductAsync();
        _accessor.HttpContext = null;

        product.Name = "Renamed";
        await _context.SaveChangesAsync();

        // 非 HTTP 场景（后台任务等）没有 AuditOperation 承载，直接丢弃
        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task EnableOperationAudit_False_CollectsNothing()
    {
        // 实体条目唯一出路是挂在 AuditOperation 上；操作审计关闭时采集注定被丢弃，
        // 采集门（AuditOperationGate）必须直接跳过，不做无谓快照
        var product = await SeedProductAsync();
        _options.EnableOperationAudit = false;

        product.Name = "Renamed";
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task ExcludedPath_CollectsNothing()
    {
        // 命中 ExcludedPaths 的请求（如 /hubs 长连接）不会产出 AuditOperation，
        // 期间的 SaveChanges 不做实体采集（与 AuditMiddleware 同一路径判定）
        var product = await SeedProductAsync();
        _accessor.HttpContext!.Request.Path = "/hubs/chat";

        product.Name = "Renamed";
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task AuditDisabledEndpoint_CollectsNothing()
    {
        // [AuditDisabled] 端点不产出 AuditOperation，实体采集同样跳过
        var product = await SeedProductAsync();
        _accessor.HttpContext!.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(new AuditDisabledAttribute()),
            "audit-disabled-endpoint"));

        product.Name = "Renamed";
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task AuditEntities_AreExcluded_ToPreventRecursion()
    {
        _context.AuditOperations.Add(new AuditOperation
        {
            FunctionName = "Test.Function",
            StartTime = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task LongPropertyValue_IsTruncatedTo4000Chars()
    {
        var product = new EntityAuditTestProduct { Name = "Widget", Description = new string('x', 6000) };
        _context.Products.Add(product);

        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        var property = entry.PropertyEntries.Single(p => p.PropertyName == nameof(EntityAuditTestProduct.Description));
        property.NewValue!.Length.ShouldBe(4000);
    }

    [Fact]
    public async Task ModifiedWithoutRealChange_IsNotCaptured()
    {
        var product = await SeedProductAsync();

        // 显式标脏但值未变化（IsModified=true、原值==新值）→ 无审计价值
        _context.Entry(product).Property(p => p.Name).IsModified = true;
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task AuditIgnoredProperty_ValueNeverRecorded()
    {
        // Added：[AuditIgnore] 属性不出现在属性列表（其余属性正常记录）
        var product = new EntityAuditTestProduct { Name = "Widget", ClientSecret = "raw-refresh-token" };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var addedEntry = _collector.Drain().ShouldHaveSingleItem();
        addedEntry.PropertyEntries.ShouldNotContain(p => p.PropertyName == nameof(EntityAuditTestProduct.ClientSecret));
        addedEntry.PropertyEntries.ShouldContain(p => p.PropertyName == nameof(EntityAuditTestProduct.Name));

        // Modified 且仅该属性变化：无可记录属性 → 整条不采集
        product.ClientSecret = "rotated-token";
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public async Task AuditIgnoredEntity_IsNeverCaptured()
    {
        _context.Set<AuditIgnoredVault>().Add(new AuditIgnoredVault { Name = "vault" });
        await _context.SaveChangesAsync();

        _collector.HasEntries.ShouldBeFalse();
    }

    [Fact]
    public void SensitiveFieldsDefaults_CoverIdentityCredentialProperties()
    {
        // PasswordHash/SecurityStamp 是 IdentityUser 继承属性，无法打 [AuditIgnore]，
        // 必须靠 SensitiveFields 默认名单掩码（记录"变了"但值打码）
        var defaults = new AuditOptions().SensitiveFields;
        defaults.ShouldContain("PasswordHash");
        defaults.ShouldContain("SecurityStamp");
    }

    [Fact]
    public async Task SaveFailure_DiscardsCapturedSnapshot()
    {
        await SeedProductAsync("Duplicate");

        // 唯一索引冲突 → SaveChanges 失败 → 快照必须被丢弃
        _context.Products.Add(new EntityAuditTestProduct { Name = "Duplicate" });
        await Should.ThrowAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        _collector.HasEntries.ShouldBeFalse();
    }
}
