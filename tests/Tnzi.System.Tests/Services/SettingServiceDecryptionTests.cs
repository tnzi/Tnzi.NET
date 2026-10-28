namespace Tnzi.System.Tests.Services;

using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

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

    private SettingService CreateService(bool withEncryptor = true, IEnumerable<ISettingProvider>? providers = null)
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
            providers ?? Enumerable.Empty<ISettingProvider>(),
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

    // ── 其余读取路径：同一个口径 ────────────────────────────────────────────

    private const string Group = "Mail";
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Setting GroupedEncryptedRow(string cipherText) => new()
    {
        Id = Guid.NewGuid(),
        Key = Key,
        Value = cipherText,
        IsEncrypted = true,
        Group = Group,
        Scope = SettingScope.Global,
    };

    /// <summary>
    /// 文档里加密值的「单点读取」入口。此前加密器缺席时答 <c>Ok(null)</c>：消费方读 API Key 拿到 null，
    /// 表现为「未配置」而走默认或降级逻辑。
    /// </summary>
    [Fact]
    public async Task GetDecrypted_without_an_encryptor_fails_instead_of_answering_null()
    {
        SeedRows(GroupedEncryptedRow("cipher"));

        var result = await CreateService(withEncryptor: false).GetDecryptedAsync(Group, Key);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_MISSING);
    }

    [Fact]
    public async Task GetDecrypted_with_a_wrong_key_fails_with_a_result_not_an_exception()
    {
        SeedRows(GroupedEncryptedRow("garbage"));
        _encryptor.Setup(e => e.Decrypt("garbage")).Throws(new CryptographicException("bad padding"));

        var result = await CreateService().GetDecryptedAsync(Group, Key);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_INVALID);
    }

    [Fact]
    public async Task GetDecrypted_returns_the_plaintext()
    {
        SeedRows(GroupedEncryptedRow("cipher"));
        _encryptor.Setup(e => e.Decrypt("cipher")).Returns("plain");

        (await CreateService().GetDecryptedAsync(Group, Key)).Data.ShouldBe("plain");
    }

    [Fact]
    public async Task A_scoped_read_without_an_encryptor_fails_instead_of_answering_null()
    {
        SeedRows(new Setting { Id = Guid.NewGuid(), Key = Key, Value = "cipher", IsEncrypted = true, Scope = SettingScope.User, ScopeId = UserId.ToString() });

        var result = await CreateService(withEncryptor: false).GetSettingAsync(Key, SettingScope.User, UserId.ToString());

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_MISSING);
    }

    /// <summary>分层解析：用户级加密值 + 全局明文值。</summary>
    private IEnumerable<ISettingProvider> LayeredProviders(bool withEncryptor)
    {
        SeedRows(
            new Setting { Id = Guid.NewGuid(), Key = Key, Value = "user-cipher", IsEncrypted = true, Scope = SettingScope.User, ScopeId = UserId.ToString() },
            new Setting { Id = Guid.NewGuid(), Key = Key, Value = "global-plain", Scope = SettingScope.Global });

        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(UserId);
        var encryptor = withEncryptor ? _encryptor.Object : null;

        return
        [
            new UserSettingProvider(_repository.Object, user.Object, encryptor),
            new GlobalSettingProvider(_repository.Object, encryptor),
        ];
    }

    /// <summary>
    /// 此前用户级 provider 在加密器缺席时返回 null，解析链悄悄落到全局的另一个值上：调用方以为读到了这个用户的设置。
    /// </summary>
    [Fact]
    public async Task Layered_resolution_does_not_fall_through_to_a_lower_scope_when_a_value_cannot_be_decrypted()
    {
        var service = CreateService(withEncryptor: false, providers: LayeredProviders(withEncryptor: false));

        var result = await service.GetSettingValueAsync(Key, "fallback");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_MISSING);
    }

    /// <summary>此前解密异常被整段 catch 吞掉、回默认值，与「没配」无从区分。</summary>
    [Fact]
    public async Task Layered_resolution_fails_instead_of_answering_the_default_when_decryption_fails()
    {
        _encryptor.Setup(e => e.Decrypt("user-cipher")).Throws(new CryptographicException("bad padding"));
        var service = CreateService(providers: LayeredProviders(withEncryptor: true));

        var result = await service.GetSettingValueAsync(Key, "fallback");

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_INVALID);
    }

    [Fact]
    public async Task Layered_resolution_returns_the_decrypted_user_value()
    {
        _encryptor.Setup(e => e.Decrypt("user-cipher")).Returns("user-plain");
        var service = CreateService(providers: LayeredProviders(withEncryptor: true));

        (await service.GetSettingValueAsync(Key, "fallback")).Data.ShouldBe("user-plain");
    }

    /// <summary>
    /// 全局级 provider 同一口径：它是链上最后一级，此前加密器缺席时返回 null，于是整条链答默认值，
    /// 与「这个键没配」一模一样。
    /// </summary>
    [Fact]
    public async Task Layered_resolution_fails_when_the_global_value_cannot_be_decrypted()
    {
        SeedRows(EncryptedRow("global-cipher"));
        var service = CreateService(withEncryptor: false, providers: [new GlobalSettingProvider(_repository.Object)]);

        var result = await service.GetSettingValueAsync(Key, "fallback");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_MISSING);
    }

    /// <summary>租户级 provider：租户自己的加密值解不开时，不能悄悄落到全局的另一个值上。</summary>
    [Fact]
    public async Task Layered_resolution_does_not_fall_through_from_an_unreadable_tenant_value_to_the_global_one()
    {
        var tenantId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        SeedRows(
            new Setting { Id = Guid.NewGuid(), Key = Key, Value = "tenant-cipher", IsEncrypted = true, Scope = SettingScope.Tenant, ScopeId = tenantId.ToString() },
            new Setting { Id = Guid.NewGuid(), Key = Key, Value = "global-plain", Scope = SettingScope.Global });
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(t => t.Id).Returns(tenantId);
        var service = CreateService(withEncryptor: false, providers:
        [
            new TenantSettingProvider(_repository.Object, tenant.Object),
            new GlobalSettingProvider(_repository.Object),
        ]);

        var result = await service.GetSettingValueAsync(Key, "fallback");

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.CONFIGURATION_MISSING);
    }

    /// <summary>「读不到」（库故障）仍沿用回默认值的既定取舍。</summary>
    [Fact]
    public async Task Layered_resolution_still_answers_the_default_when_the_store_cannot_be_read()
    {
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Throws(new InvalidOperationException("database down"));
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Id).Returns(UserId);
        var service = CreateService(providers:
        [
            new UserSettingProvider(_repository.Object, user.Object, _encryptor.Object),
        ]);

        var result = await service.GetSettingValueAsync(Key, "fallback");

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe("fallback");
    }
}
