using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tnzi.EFCore.Extensions;
using Tnzi.Security;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// 信封模式的密文更长(带封装的数据密钥),<c>IsEncrypted()</c> 放宽的列长必须按它算,
/// 否则最坏情况的值在数据库层才写不进去。
/// </summary>
public sealed class EnvelopeEncryptedColumnLengthTests : IDisposable
{
    private const int DeclaredLength = 50;

    private static readonly string LongestKeyId = new('k', FieldEncryptionOptions.MaxKeyIdLength);

    /// <summary>进程级单例:值转换器在模型构建期捕获加密器,EF 按上下文类型缓存模型。</summary>
    private static readonly EnvelopeFieldEncryptor Encryptor = CreateEncryptor();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _serviceProvider;

    public EnvelopeEncryptedColumnLengthTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IFieldEncryptor>(Encryptor);
        services.AddDbContext<EnvelopeEncryptedLengthDbContext>(options => options.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();
    }

    public static TheoryData<string, string> WorstCaseValues => new()
    {
        { "ascii", new string('a', DeclaredLength) },
        { "cjk", new string('中', DeclaredLength) },
        { "supplementary", string.Concat(Enumerable.Repeat("\U0001F600", DeclaredLength)) },
    };

    [Theory]
    [MemberData(nameof(WorstCaseValues))]
    public void WidenedLength_HoldsTheEnvelopeCiphertextOfAnyValueWithinTheDeclaredLength(string kind, string plaintext)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EnvelopeEncryptedLengthDbContext>();
        var property = context.Model.FindEntityType(typeof(EncryptedLengthNote))!.FindProperty(nameof(EncryptedLengthNote.Address))!;

        var ciphertext = (string)property.GetValueConverter()!.ConvertToProvider(plaintext)!;

        Assert.StartsWith(LongestKeyId + ":e1:", ciphertext);
        Assert.True(ciphertext.Length <= property.GetMaxLength(),
            $"{kind}: ciphertext of {ciphertext.Length} chars does not fit the widened length {property.GetMaxLength()}");
    }

    private static EnvelopeFieldEncryptor CreateEncryptor()
    {
        var options = new FieldEncryptionOptions
        {
            Enabled = true,
            Mode = FieldEncryptionMode.Envelope,
            ActiveKeyId = LongestKeyId,
            Keys = new Dictionary<string, string> { [LongestKeyId] = AesGcmHelper.GenerateKeyBase64() },
        };
        return new EnvelopeFieldEncryptor(new FieldEncryptionKeyRing(new MutableFieldEncryptionOptionsMonitor(options)));
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}

public class EnvelopeEncryptedLengthDbContext : TnziDbContext<EnvelopeEncryptedLengthDbContext>
{
    public EnvelopeEncryptedLengthDbContext(DbContextOptions<EnvelopeEncryptedLengthDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new NoteConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>私有嵌套:程序集扫描跳过它,只由本上下文经框架的实体注册入口套用。</summary>
    private sealed class NoteConfiguration : EntityTypeConfigurationBase<EncryptedLengthNote, Guid>
    {
        public override Type? DbContextType => typeof(EnvelopeEncryptedLengthDbContext);

        public override void Configure(EntityTypeBuilder<EncryptedLengthNote> builder)
        {
            builder.ToTable("EnvelopeEncryptedLengthNotes");
            builder.Property(x => x.Address!).HasMaxLength(50).IsEncrypted();
        }
    }
}
