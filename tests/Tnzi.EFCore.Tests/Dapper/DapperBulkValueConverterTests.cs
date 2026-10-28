using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using Tnzi.EFCore.Dapper;
using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests.Dapper;

/// <summary>
/// Dapper 批量写入绕过变更跟踪器，但列值必须仍经 EF 的值转换器转成提供程序值（与 <c>SaveChanges</c> 同源）。
/// </summary>
/// <remarks>
/// <para>
/// 字段级加密只挂在值转换器上。批量路径此前以 <c>PropertyInfo.GetValue</c> 的 CLR 原值直接作参数：
/// <c>IsEncrypted()</c> 的列以明文落库，而读侧转换器对不像密文的值原样返回（迁移期的刻意设计），
/// 于是界面一切正常、没有任何告警。<c>HasConversion&lt;string&gt;()</c> 的枚举列同理被写成整数。
/// </para>
/// <para>
/// 真实 SQLite + 真实 <see cref="AesGcmFieldEncryptor"/> + 真实 <c>IsEncrypted()</c>，断言落库的原始列值，
/// 不 Mock 被测对象。
/// </para>
/// </remarks>
public sealed class DapperBulkValueConverterTests : IDisposable
{
    /// <summary>
    /// 进程级单例：值转换器在模型构建期捕获加密器，而 EF 按上下文类型缓存模型，
    /// 每个用例各造一个只会让后面的用例用上第一个用例的实例。
    /// </summary>
    private static readonly AesGcmFieldEncryptor Encryptor = CreateEncryptor();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly ConverterBulkDbContext _dbContext;

    public DapperBulkValueConverterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IFieldEncryptor>(Encryptor);
        services.AddDbContext<ConverterBulkDbContext>(options => options.UseSqlite(_connection));

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ConverterBulkDbContext>();
        _dbContext.Database.EnsureCreated();
    }

    private DapperService CreateService() => new(_dbContext, new SqliteTestProvider());

    [Fact]
    public async Task BulkInsertAsync_EncryptedColumn_IsStoredAsCiphertext()
    {
        var note = new ConverterBulkNote { Title = "note", Body = "home address 12 Elm St", Level = ConverterBulkLevel.High };

        await CreateService().BulkInsertAsync([note]);

        var stored = await ReadRawAsync(note.Id);
        Assert.NotEqual(note.Body, stored.Body);
        Assert.True(Encryptor.IsEncrypted(stored.Body!), $"Body was stored as '{stored.Body}'");

        var reloaded = await _dbContext.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        Assert.Equal("home address 12 Elm St", reloaded.Body);
    }

    [Fact]
    public async Task BulkInsertAsync_NullEncryptedColumn_StaysNull()
    {
        var note = new ConverterBulkNote { Title = "no body", Body = null, Level = ConverterBulkLevel.Low };

        await CreateService().BulkInsertAsync([note]);

        var stored = await ReadRawAsync(note.Id);
        Assert.Null(stored.Body);
    }

    [Fact]
    public async Task BulkUpdateAsync_EncryptedColumn_IsStoredAsCiphertext()
    {
        var note = new ConverterBulkNote { Title = "note", Body = "original", Level = ConverterBulkLevel.Low };
        _dbContext.Notes.Add(note);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var detached = new ConverterBulkNote { Id = note.Id, Title = "note", Body = "rewritten secret", Level = ConverterBulkLevel.High };

        await CreateService().BulkUpdateAsync([detached]);

        var stored = await ReadRawAsync(note.Id);
        Assert.NotEqual("rewritten secret", stored.Body);
        Assert.True(Encryptor.IsEncrypted(stored.Body!), $"Body was stored as '{stored.Body}'");

        var reloaded = await _dbContext.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        Assert.Equal("rewritten secret", reloaded.Body);
    }

    [Fact]
    public async Task BulkInsertAsync_EnumStoredAsString_WritesTheName()
    {
        var note = new ConverterBulkNote { Title = "enum", Level = ConverterBulkLevel.High };

        await CreateService().BulkInsertAsync([note]);

        var stored = await ReadRawAsync(note.Id);
        Assert.Equal("High", stored.Level);
        var reloaded = await _dbContext.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        Assert.Equal(ConverterBulkLevel.High, reloaded.Level);
    }

    [Fact]
    public async Task BulkUpdateAsync_EnumStoredAsString_WritesTheName()
    {
        var note = new ConverterBulkNote { Title = "enum", Level = ConverterBulkLevel.Low };
        _dbContext.Notes.Add(note);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        await CreateService().BulkUpdateAsync([new ConverterBulkNote { Id = note.Id, Title = "enum", Level = ConverterBulkLevel.High }]);

        var stored = await ReadRawAsync(note.Id);
        Assert.Equal("High", stored.Level);
    }

    /// <summary>
    /// 失败关闭：影子属性在 CLR 对象上没有值可取，批量路径写不了它。
    /// 必须在发出任何 SQL 之前点名拒绝，而不是生成一条缺参数的语句。
    /// </summary>
    [Fact]
    public async Task BulkInsertAsync_EntityWithShadowProperty_ThrowsNamingTheProperty()
    {
        var tagged = new ConverterBulkTagged { Name = "shadow" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().BulkInsertAsync([tagged]));

        Assert.Contains(ConverterBulkDbContext.ShadowPropertyName, ex.Message);
        Assert.Equal(0, await _dbContext.Tagged.CountAsync());
    }

    private async Task<(string? Body, string? Level)> ReadRawAsync(Guid id)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT \"Body\", \"Level\" FROM \"ConverterBulkNotes\" WHERE \"Id\" = $id";
        // EF 的 SQLite 提供程序把 Guid 存成大写 TEXT
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Row {id} was not found");
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static AesGcmFieldEncryptor CreateEncryptor()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var options = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(key) },
        };
        return new AesGcmFieldEncryptor(new FixedOptionsMonitor(options));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedOptionsMonitor(FieldEncryptionOptions value) : IOptionsMonitor<FieldEncryptionOptions>
    {
        public FieldEncryptionOptions CurrentValue => value;

        public FieldEncryptionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<FieldEncryptionOptions, string?> listener) => null;
    }
}

public enum ConverterBulkLevel
{
    Low = 1,
    High = 2,
}

/// <summary>带加密列与「枚举存字符串」列的测试实体。</summary>
public class ConverterBulkNote : EntityBase<Guid>
{
    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public ConverterBulkLevel Level { get; set; }
}

/// <summary>带影子属性的测试实体。</summary>
public class ConverterBulkTagged : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;
}

public class ConverterBulkDbContext : TnziDbContext<ConverterBulkDbContext>
{
    public const string ShadowPropertyName = "LegacyTag";

    public ConverterBulkDbContext(DbContextOptions<ConverterBulkDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<ConverterBulkNote> Notes => Set<ConverterBulkNote>();

    public DbSet<ConverterBulkTagged> Tagged => Set<ConverterBulkTagged>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new ConverterBulkNoteConfiguration().RegisterTo(modelBuilder, this);

        modelBuilder.Entity<ConverterBulkTagged>(b =>
        {
            b.ToTable("ConverterBulkTagged");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property<string>(ShadowPropertyName).HasMaxLength(50);
        });

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// 经框架的实体注册入口配置（<c>IsEncrypted()</c> 只能在那里解析出加密器）。
    /// 私有嵌套：程序集扫描跳过它，不会被别的用例里的实体发现带走。
    /// </summary>
    private sealed class ConverterBulkNoteConfiguration : EntityTypeConfigurationBase<ConverterBulkNote, Guid>
    {
        public override Type? DbContextType => typeof(ConverterBulkDbContext);

        public override void Configure(EntityTypeBuilder<ConverterBulkNote> builder)
        {
            builder.ToTable("ConverterBulkNotes");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Title).HasMaxLength(100);
            builder.Property(x => x.Body!).IsEncrypted();
            builder.Property(x => x.Level).HasConversion<string>().HasMaxLength(16);
        }
    }
}
