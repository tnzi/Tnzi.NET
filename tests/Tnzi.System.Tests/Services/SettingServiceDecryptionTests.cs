namespace Tnzi.System.Tests.Services;

/// <summary>
/// <see cref="SettingService.GetSettingAsync(string, string?)"/> 对「读到了密文却解不开」的处置。
/// </summary>
/// <remarks>
/// 缺陷形态：整段读取包在 <c>catch (Exception)</c> 里回默认值。密钥轮换、密文被明文覆盖、加密被关掉 ——
/// 这三种情况下调用方拿到的都是默认值加一条 Warning，行为上与「这个键没配」一模一样。
/// 「读不到」（库 / 缓存故障）沿用回默认值的既定取舍；「读到了但解不开」必须是一个失败的 Result。
/// </remarks>
public class SettingServiceDecryptionTests
{
    private const string Key = "Mail.Password";

    private readonly Mock<IRepository<Setting, Guid>> _repository = new();
    private readonly Mock<ISettingEncryptor> _encryptor = new();

    private SettingService CreateService(bool withEncryptor = true)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(new LoggerFactory());

        var applicationOptions = new Mock<IOptionsMonitor<ApplicationOptions>>();
        applicationOptions.SetupGet(x => x.CurrentValue).Returns(new ApplicationOptions());

        // 松散 mock：GetAsync<SettingCacheEntry> 未设置即返回 null = 缓存未命中，走库。
        var cache = new Mock<ICache>();

        return new SettingService(
            serviceProvider.Object,
            _repository.Object,
            applicationOptions.Object,
            Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions { Enabled = true }),
            cache.Object,
            Enumerable.Empty<ISettingProvider>(),
            Enumerable.Empty<ISettingDefinitionProvider>(),
            withEncryptor ? _encryptor.Object : null);
    }

    private void SeedRows(params Setting[] rows)
    {
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(rows.ToList().BuildMock());
    }

    private static Setting EncryptedRow(string cipherText) => new()
    {
        Id = Guid.NewGuid(),
        Key = Key,
        Value = cipherText,
        IsEncrypted = true,
        Scope = SettingScope.Global,
    };

    /// <summary>★现形用例：此前返回 <c>Ok(default)</c>，与「键不存在」无法区分。</summary>
    [Fact]
    public async Task A_row_that_cannot_be_decrypted_fails_instead_of_answering_the_default()
    {
        SeedRows(EncryptedRow("garbage"));
        _encryptor
            .Setup(e => e.Decrypt("garbage"))
            .Throws(new BusinessException("Failed to decrypt setting value: invalid key or corrupted data", ErrorCodes.VALIDATION_ERROR));
        var service = CreateService();

        var result = await service.GetSettingAsync(Key, "fallback");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.Data.ShouldBeNull();
        result.Message!.ShouldContain(Key);
    }

    [Fact]
    public async Task An_encrypted_row_without_an_encryptor_fails_instead_of_answering_null()
    {
        SeedRows(EncryptedRow("cipher"));
        var service = CreateService(withEncryptor: false);

        var result = await service.GetSettingAsync(Key, "fallback");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.Message!.ShouldContain("encryption");
    }

    [Fact]
    public async Task The_typed_overload_propagates_the_failure()
    {
        SeedRows(EncryptedRow("garbage"));
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Throws(new BusinessException("bad", ErrorCodes.VALIDATION_ERROR));
        var service = CreateService();

        var result = await service.GetSettingAsync<int>(Key, 7);

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task A_row_that_decrypts_returns_the_plaintext()
    {
        SeedRows(EncryptedRow("cipher"));
        _encryptor.Setup(e => e.Decrypt("cipher")).Returns("plain");
        var service = CreateService();

        var result = await service.GetSettingAsync(Key);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe("plain");
    }

    [Fact]
    public async Task A_missing_key_still_answers_the_default()
    {
        SeedRows();
        var service = CreateService();

        var result = await service.GetSettingAsync(Key, "fallback");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe("fallback");
    }
}
