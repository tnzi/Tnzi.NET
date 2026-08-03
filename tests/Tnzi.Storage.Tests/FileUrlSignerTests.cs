namespace Tnzi.Storage.Tests;

/// <summary>
/// 签名算法本身的回归网。这些用例守的是「令牌不能被改造成别的文件 / 别的过期时间」——
/// 一旦破，私密文件就退回成"知道 id 就能下载"。
/// </summary>
public class FileUrlSignerTests
{
    private static readonly Guid FileA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FileB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static FileUrlSigner CreateSut(string? signingKey = "unit-test-signing-key", string? jwtKey = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new StorageOptions { UrlSigningKey = signingKey });
        var settings = new Dictionary<string, string?>();
        if (jwtKey != null)
            settings["Identity:Jwt:SecretKey"] = jwtKey;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new FileUrlSigner(options, configuration, NullLogger<FileUrlSigner>.Instance);
    }

    [Fact]
    public void AFreshToken_ValidatesForItsOwnFile()
    {
        var sut = CreateSut();
        var token = sut.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), userId: null);

        Assert.True(sut.TryValidate(FileA, token, out _));
    }

    [Fact]
    public void AToken_DoesNotValidateForAnotherFile()
    {
        // fileId 进签名载荷（虽然不进令牌文本），所以 A 的令牌换不到 B。
        var sut = CreateSut();
        var token = sut.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), userId: null);

        Assert.False(sut.TryValidate(FileB, token, out _));
    }

    [Fact]
    public void AnExpiredToken_IsRejected()
    {
        var sut = CreateSut();
        var token = sut.Sign(FileA, DateTimeOffset.UtcNow.AddSeconds(-1), userId: null);

        Assert.False(sut.TryValidate(FileA, token, out _));
    }

    [Fact]
    public void TheExpiryCannotBePushedOutByEditingTheToken()
    {
        // 过期时间在签名载荷里。把它改大而不重算签名，验签就会失败 —— 否则令牌等于永久有效。
        var sut = CreateSut();
        var token = sut.Sign(FileA, DateTimeOffset.UtcNow.AddSeconds(-1), userId: null);
        var parts = token.Split('.');
        var tampered = string.Join('.', parts[0], DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds(), parts[2], parts[3]);

        Assert.False(sut.TryValidate(FileA, tampered, out _));
    }

    [Fact]
    public void ATokenSignedWithAnotherKey_IsRejected()
    {
        var mint = CreateSut("key-one");
        var verify = CreateSut("key-two");
        var token = mint.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), userId: null);

        Assert.False(verify.TryValidate(FileA, token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("1.999.")]                 // 段数不足
    [InlineData("2.999..sig")]             // 版本不认识
    [InlineData("1.not-a-number..sig")]    // 过期时间不是数字
    public void MalformedTokens_AreRejectedWithoutThrowing(string? token)
    {
        var sut = CreateSut();
        Assert.False(sut.TryValidate(FileA, token, out _));
    }

    [Fact]
    public void TheMintingUser_IsCarriedThroughForAuditing()
    {
        var sut = CreateSut();
        var user = Guid.NewGuid();
        var token = sut.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), user);

        Assert.True(sut.TryValidate(FileA, token, out var carried));
        Assert.Equal(user, carried);
    }

    [Fact]
    public void WithoutAnExplicitKey_ItDerivesOneFromTheJwtSecret()
    {
        // 绝大多数部署已经配了 JWT 密钥（全实例共享），复用它让签名零配置就正确工作。
        // 两个独立实例必须互相认账，否则多实例部署下"图片有时候能看有时候不能"。
        var instanceOne = CreateSut(signingKey: null, jwtKey: "shared-jwt-secret");
        var instanceTwo = CreateSut(signingKey: null, jwtKey: "shared-jwt-secret");
        var token = instanceOne.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), userId: null);

        Assert.True(instanceTwo.TryValidate(FileA, token, out _));
    }

    [Fact]
    public void WithNoKeysAtAll_EachProcessGetsItsOwnRandomKey()
    {
        // 这是最后的回退，会记 Warning：单实例可用，多实例互不认账。
        var instanceOne = CreateSut(signingKey: null, jwtKey: null);
        var instanceTwo = CreateSut(signingKey: null, jwtKey: null);
        var token = instanceOne.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), userId: null);

        Assert.True(instanceOne.TryValidate(FileA, token, out _));
        Assert.False(instanceTwo.TryValidate(FileA, token, out _));
    }

    [Fact]
    public void SignaturePart_IsUnpaddedUrlSafeBase64()
    {
        // 令牌整体进 query（`?sig=`），签名段必须是无填充的 URL 安全编码：
        // 出现 '+' / '/' / '=' 会被 URL 编码或被中间层规范化掉，表现为「有的图能看有的不能」。
        // 这条守卫钉死的是**线缆格式**——换编码实现（手写 ↔ BCL Base64Url）必须不改变输出，
        // 否则已签发的令牌全部作废，而往返测试因两端一起变而看不出来。
        var sut = CreateSut();
        var token = sut.Sign(FileA, DateTimeOffset.UtcNow.AddMinutes(10), userId: null);

        var signature = token.Split('.')[^1];
        Assert.DoesNotContain('+', signature);
        Assert.DoesNotContain('/', signature);
        Assert.DoesNotContain('=', signature);
        // HMAC-SHA256 = 32 字节 → base64 43 字符（无填充）
        Assert.Equal(43, signature.Length);
        Assert.Equal(signature, Uri.EscapeDataString(signature));
    }
}
