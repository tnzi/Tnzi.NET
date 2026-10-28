using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// 设计期（<c>dotnet ef</c>）建模时 <c>IsEncrypted()</c> 不需要真正的加密器。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 加密器只从上下文的应用容器里取，取不到就抛「未启用字段加密」。框架的设计期工厂建上下文时没有应用容器，
/// 于是用了 <c>IsEncrypted()</c> 的应用在推荐的迁移流程里 <c>migrations add</c> 直接失败，即使配置里加密已经打开。
/// 设计期只建模、不读写数据：给一个调用即抛的占位加密器，模型（含放宽后的列长）与运行期一致；
/// 运行期取不到加密器仍然抛，绝不降级为明文。
/// </para>
/// <para>
/// <see cref="EF.IsDesignTime"/> 是进程级静态，用例归入不并行的集合并在 finally 里复位。
/// </para>
/// </remarks>
[Collection(DesignTimeStaticsCollection.Name)]
public class EncryptedPropertyDesignTimeTests
{
    [Fact]
    public void DesignTimeFactory_BuildsTheModel_WithoutAnEncryptor()
    {
        var original = EF.IsDesignTime;
        try
        {
            EF.IsDesignTime = true;
            using var settings = CreateSettings();

            using var context = new RoutingDesignTimeFactory<EncryptedDesignTimeDbContext>(settings.ConfigurationDirectory).CreateDbContext([]);

            var property = context.Model.FindEntityType(typeof(EncryptedDesignTimeNote))!.FindProperty(nameof(EncryptedDesignTimeNote.Secret))!;

            // 与运行期同一套列长放宽：64（密钥标识上限）+ 4（版本前缀与冒号）+ Base64(32 × 4 + 28) = 276
            Assert.Equal(276, property.GetMaxLength());
            Assert.NotNull(property.GetValueConverter());
        }
        finally
        {
            EF.IsDesignTime = original;
        }
    }

    /// <summary>设计期的占位加密器一旦真被调用（例如种子数据要落密文）就响亮失败，不产出明文。</summary>
    [Fact]
    public void DesignTimeEncryptor_RefusesToConvertValues()
    {
        var original = EF.IsDesignTime;
        try
        {
            EF.IsDesignTime = true;
            using var settings = CreateSettings();

            using var context = new RoutingDesignTimeFactory<EncryptedDesignTimeDbContext>(settings.ConfigurationDirectory).CreateDbContext([]);
            var converter = context.Model.FindEntityType(typeof(EncryptedDesignTimeNote))!
                .FindProperty(nameof(EncryptedDesignTimeNote.Secret))!.GetValueConverter()!;

            Assert.Throws<InvalidOperationException>(() => converter.ConvertToProvider("plain"));
        }
        finally
        {
            EF.IsDesignTime = original;
        }
    }

    /// <summary>对照组：不在设计期、又取不到加密器时照旧抛异常，绝不静默落明文。</summary>
    [Fact]
    public void OutsideDesignTime_MissingEncryptor_StillThrows()
    {
        var original = EF.IsDesignTime;
        try
        {
            EF.IsDesignTime = false;
            using var settings = CreateSettings();

            using var context = new RoutingDesignTimeFactory<EncryptedDesignTimeDbContext>(settings.ConfigurationDirectory).CreateDbContext([]);

            var ex = Assert.Throws<InvalidOperationException>(() => context.Model);
            Assert.Contains("IFieldEncryptor", ex.Message);
        }
        finally
        {
            EF.IsDesignTime = original;
        }
    }

    private static TemporaryAppSettings CreateSettings() => new(new
    {
        EFCore = new { FieldEncryption = new { Enabled = true } },
        Database = new
        {
            DbContexts = new[]
            {
                new { Name = "Default", ConnectionString = "Data Source=:memory:", Provider = "Sqlite" },
            },
        },
    });
}

public class EncryptedDesignTimeNote : EntityBase<Guid>
{
    public string Secret { get; set; } = string.Empty;
}

public class EncryptedDesignTimeDbContext : TnziDbContext<EncryptedDesignTimeDbContext>
{
    public EncryptedDesignTimeDbContext(DbContextOptions<EncryptedDesignTimeDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new NoteConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>私有嵌套：程序集扫描跳过它，只由本上下文经框架的实体注册入口套用（IsEncrypted 只在那里解析加密器）。</summary>
    private sealed class NoteConfiguration : EntityTypeConfigurationBase<EncryptedDesignTimeNote, Guid>
    {
        public override Type? DbContextType => typeof(EncryptedDesignTimeDbContext);

        public override void Configure(EntityTypeBuilder<EncryptedDesignTimeNote> builder)
        {
            builder.ToTable("EncryptedDesignTimeNotes");
            builder.Property(x => x.Secret).HasMaxLength(32).IsEncrypted();
        }
    }
}
