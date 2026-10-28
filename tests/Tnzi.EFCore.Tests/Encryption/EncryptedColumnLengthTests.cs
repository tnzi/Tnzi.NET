using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// <c>IsEncrypted()</c> 放宽后的列长装得下原列长内任何文本的密文，包括每字符多字节的文本与最长的密钥标识。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前按每字符 1 字节估算（<c>(N + 64) × 4 / 3</c>）：<c>HasMaxLength(50)</c> 放宽到 152，
/// 而加密的是 UTF-8 字节，汉字每字 3 字节，30 个汉字的密文已超过 160 个字符，
/// 中文地址写到原长的三分之一左右就在数据库层报超长，正是放宽列长要避免的那种失败。
/// </para>
/// <para>
/// 真实 <see cref="AesGcmFieldEncryptor"/> 产出密文，与模型上的列长直接比较（SQLite 不按列长截断，断言不能交给数据库）。
/// </para>
/// </remarks>
public sealed class EncryptedColumnLengthTests : IDisposable
{
    private const int DeclaredLength = 50;

    /// <summary>校验器允许的最长密钥标识：它写进每一条密文。</summary>
    private static readonly string LongestKeyId = new('k', FieldEncryptionOptions.MaxKeyIdLength);

    /// <summary>进程级单例：值转换器在模型构建期捕获加密器，EF 按上下文类型缓存模型。</summary>
    private static readonly AesGcmFieldEncryptor Encryptor = CreateEncryptor();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _serviceProvider;

    public EncryptedColumnLengthTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IFieldEncryptor>(Encryptor);
        services.AddDbContext<EncryptedLengthDbContext>(options => options.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();
    }

    public static TheoryData<string, string> WorstCaseValues => new()
    {
        { "ascii", new string('a', DeclaredLength) },
        { "cjk", new string('中', DeclaredLength) },
        // 增补字符：按 UTF-16 计 2 个单位、4 个 UTF-8 字节；按码点计长度的库允许 50 个
        { "supplementary", string.Concat(Enumerable.Repeat("\U0001F600", DeclaredLength)) },
    };

    [Theory]
    [MemberData(nameof(WorstCaseValues))]
    public void WidenedLength_HoldsTheCiphertextOfAnyValueWithinTheDeclaredLength(string kind, string plaintext)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptedLengthDbContext>();
        var property = context.Model.FindEntityType(typeof(EncryptedLengthNote))!.FindProperty(nameof(EncryptedLengthNote.Address))!;

        var ciphertext = (string)property.GetValueConverter()!.ConvertToProvider(plaintext)!;

        Assert.StartsWith(LongestKeyId + ":", ciphertext);
        Assert.True(ciphertext.Length <= property.GetMaxLength(),
            $"{kind}: ciphertext of {ciphertext.Length} chars does not fit the widened length {property.GetMaxLength()}");
    }

    [Fact]
    public void Validator_RejectsKeyIdsLongerThanTheReservedLength()
    {
        var validator = new FieldEncryptionOptionsValidator();
        var tooLong = new string('k', FieldEncryptionOptions.MaxKeyIdLength + 1);
        var options = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = tooLong,
            Keys = new Dictionary<string, string> { [tooLong] = NewKeyMaterial() },
        };

        Assert.True(validator.Validate(null, options).Failed);
    }

    private static AesGcmFieldEncryptor CreateEncryptor()
    {
        var options = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = LongestKeyId,
            Keys = new Dictionary<string, string> { [LongestKeyId] = NewKeyMaterial() },
        };
        Assert.True(new FieldEncryptionOptionsValidator().Validate(null, options).Succeeded);
        return new AesGcmFieldEncryptor(new FixedOptionsMonitor(options));
    }

    private static string NewKeyMaterial()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }

    public void Dispose()
    {
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

public class EncryptedLengthNote : EntityBase<Guid>
{
    public string? Address { get; set; }
}

public class EncryptedLengthDbContext : TnziDbContext<EncryptedLengthDbContext>
{
    public EncryptedLengthDbContext(DbContextOptions<EncryptedLengthDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new NoteConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>私有嵌套：程序集扫描跳过它，只由本上下文经框架的实体注册入口套用（IsEncrypted 只在那里解析加密器）。</summary>
    private sealed class NoteConfiguration : EntityTypeConfigurationBase<EncryptedLengthNote, Guid>
    {
        public override Type? DbContextType => typeof(EncryptedLengthDbContext);

        public override void Configure(EntityTypeBuilder<EncryptedLengthNote> builder)
        {
            builder.ToTable("EncryptedLengthNotes");
            builder.Property(x => x.Address!).HasMaxLength(50).IsEncrypted();
        }
    }
}
