using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tnzi.Audit.Tests.TestSupport;
using Tnzi.EFCore.Extensions;
using AuditEntityState = Tnzi.Audit.Metadata.EntityState;

namespace Tnzi.Audit.Tests.Interceptors;

/// <summary>带一个 <c>IsEncrypted()</c> 列的被试实体（银行账号这类按文档推荐加密的值）。</summary>
public class EncryptedAuditTestAccount : EntityBase<Guid>
{
    public string Holder { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;
}

/// <summary>经框架的实体配置基类声明加密列：与消费方的写法逐字相同。</summary>
public class EncryptedAuditTestAccountConfiguration : EntityTypeConfigurationBase<EncryptedAuditTestAccount, Guid>
{
    /// <summary>只注册到自己的 DbContext：配置类会被实体自动发现扫到，同一测试程序集里别的 DbContext 没有加密器。</summary>
    public override Type? DbContextType => typeof(EncryptedAuditTestDbContext);

    public override void Configure(EntityTypeBuilder<EncryptedAuditTestAccount> builder)
    {
        builder.ToTable("TestEncryptedAccounts");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Holder).HasMaxLength(100);
        builder.Property(e => e.AccountNumber).HasMaxLength(64).IsEncrypted();
    }
}

public class EncryptedAuditTestDbContext : TnziDbContext<EncryptedAuditTestDbContext>
{
    public EncryptedAuditTestDbContext(DbContextOptions<EncryptedAuditTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<EncryptedAuditTestAccount> Accounts => Set<EncryptedAuditTestAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new EncryptedAuditTestAccountConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// 字段级加密列的值不得以明文进入实体级审计（<c>Audit_PropertyEntry</c>）。
/// </summary>
/// <remarks>
/// <para>
/// ★ EF 的 <c>CurrentValue</c> / <c>OriginalValue</c> 是<b>模型值</b>：<c>IsEncrypted()</c> 只是一个值转换器，
/// 转换只发生在数据库提供程序的边界上，拦截器拿到的是明文。此前拦截器只认 <c>[AuditIgnore]</c> 与按属性名的
/// <c>SensitiveFields</c>，于是一个按文档加密的银行账号每次新建 / 修改都以明文落进审计表：
/// 持 <c>audit.operation.view</c> 的人读得到，加密删除销毁了密钥之后它还在。
/// </para>
/// </remarks>
public class EntityAuditEncryptedColumnTests : IDisposable
{
    private const string Plaintext = "004-12345-0099887766";

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    private readonly SqliteConnection _connection;
    private readonly EntityAuditCollector _collector = new();
    private readonly EncryptedAuditTestDbContext _context;
    private readonly ServiceProvider _services;

    public EntityAuditEncryptedColumnTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var encryptionOptions = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(key) }
        };

        // 加密器经 DbContext 选项携带的应用容器解析（IsEncrypted() 在模型构建期就要它）。
        var appServices = new ServiceCollection();
        appServices.AddSingleton<IFieldEncryptor>(new AesGcmFieldEncryptor(new StaticOptionsMonitor<FieldEncryptionOptions>(encryptionOptions)));
        _services = appServices.BuildServiceProvider();

        var interceptor = new EntityAuditSaveChangesInterceptor(
            _collector,
            new StaticOptionsMonitor<AuditOptions>(new AuditOptions()),
            new TestHttpContextAccessor(),
            NullLogger<EntityAuditSaveChangesInterceptor>.Instance);

        var options = new DbContextOptionsBuilder<EncryptedAuditTestDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(_services)
            .AddInterceptors(interceptor)
            .Options;

        _context = new EncryptedAuditTestDbContext(options, new Mock<ICurrentUser>().Object);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Added_EncryptedColumn_IsRecordedMasked()
    {
        _context.Accounts.Add(new EncryptedAuditTestAccount { Holder = "Alice", AccountNumber = Plaintext });
        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        entry.OperationType.ShouldBe(AuditEntityState.Added);
        var property = entry.PropertyEntries.Single(p => p.PropertyName == nameof(EncryptedAuditTestAccount.AccountNumber));
        property.NewValue.ShouldBe(RequestBodyRedactor.RedactedValue);
        // 没有加密的列照常记录，审计行仍答得出「建了谁的账户」。
        entry.PropertyEntries.Single(p => p.PropertyName == nameof(EncryptedAuditTestAccount.Holder)).NewValue.ShouldBe("Alice");
    }

    [Fact]
    public async Task Modified_EncryptedColumn_RecordsThatItChanged_ButNeitherValue()
    {
        var account = new EncryptedAuditTestAccount { Holder = "Alice", AccountNumber = Plaintext };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        _collector.Drain();

        account.AccountNumber = "004-99999-0011223344";
        await _context.SaveChangesAsync();

        var property = _collector.Drain().ShouldHaveSingleItem().PropertyEntries.ShouldHaveSingleItem();
        property.PropertyName.ShouldBe(nameof(EncryptedAuditTestAccount.AccountNumber));
        property.OriginalValue.ShouldBe(RequestBodyRedactor.RedactedValue);
        property.NewValue.ShouldBe(RequestBodyRedactor.RedactedValue);
    }

    [Fact]
    public async Task Deleted_EncryptedColumn_IsRecordedMasked()
    {
        var account = new EncryptedAuditTestAccount { Holder = "Alice", AccountNumber = Plaintext };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        _collector.Drain();

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();

        var entry = _collector.Drain().ShouldHaveSingleItem();
        entry.PropertyEntries.Single(p => p.PropertyName == nameof(EncryptedAuditTestAccount.AccountNumber))
            .OriginalValue.ShouldBe(RequestBodyRedactor.RedactedValue);
    }

    /// <summary>对照：列本身确实是加密落库的，模型值确实是明文：证明上面的断言测的是拦截器而不是别的什么。</summary>
    [Fact]
    public async Task TheColumnIsEncryptedAtRest_AndTheModelValueIsPlaintext()
    {
        var account = new EncryptedAuditTestAccount { Holder = "Alice", AccountNumber = Plaintext };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT AccountNumber FROM TestEncryptedAccounts";
        var stored = (string)(await command.ExecuteScalarAsync())!;
        stored.ShouldNotContain(Plaintext);

        _context.Entry(account).Property(a => a.AccountNumber).CurrentValue.ShouldBe(Plaintext);
        _context.Model.FindEntityType(typeof(EncryptedAuditTestAccount))!
            .FindProperty(nameof(EncryptedAuditTestAccount.AccountNumber))!
            .IsEncryptedColumn().ShouldBeTrue();
        _context.Model.FindEntityType(typeof(EncryptedAuditTestAccount))!
            .FindProperty(nameof(EncryptedAuditTestAccount.Holder))!
            .IsEncryptedColumn().ShouldBeFalse();
    }
}
