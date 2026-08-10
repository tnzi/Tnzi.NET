using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Tnzi.EFCore.Tests.Encryption;

/// <summary>
/// 字段级加密器的行为与安全不变量。
/// </summary>
/// <remarks>
/// 重点不在「能加解密」，而在几条一旦破掉就会静默泄露数据的性质：
/// 用途绑定、密文不可预测、密钥缺失必须报错、篡改必须失败。
/// </remarks>
public class FieldEncryptionTests
{
    private const string KeyA = "k1";
    private const string KeyB = "k2";

    private static string NewKeyMaterial()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static AesGcmFieldEncryptor CreateEncryptor(
        out FieldEncryptionOptions options,
        string activeKeyId = KeyA,
        params string[] extraKeyIds)
    {
        options = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = activeKeyId,
            Keys = new Dictionary<string, string> { [KeyA] = NewKeyMaterial() }
        };

        foreach (var id in extraKeyIds)
        {
            options.Keys[id] = NewKeyMaterial();
        }

        var captured = options;
        var monitor = new TestOptionsMonitor(() => captured);
        return new AesGcmFieldEncryptor(monitor);
    }

    [Fact]
    public void Roundtrip_RestoresOriginalValue()
    {
        var encryptor = CreateEncryptor(out _);

        var cipher = encryptor.Encrypt("613-555-0199", "Demo.Tip.PhoneNumber");

        Assert.Equal("613-555-0199", encryptor.Decrypt(cipher, "Demo.Tip.PhoneNumber"));
    }

    [Fact]
    public void Ciphertext_DoesNotContainPlaintext()
    {
        var encryptor = CreateEncryptor(out _);

        var cipher = encryptor.Encrypt("SIN 046 454 286", "Demo.Person.Sin");

        Assert.DoesNotContain("046", cipher, StringComparison.Ordinal);
        Assert.DoesNotContain("SIN", cipher, StringComparison.Ordinal);
    }

    [Fact]
    public void SamePlaintext_ProducesDifferentCiphertextEachTime()
    {
        // 若两次加密结果相同，攻击者不必解密就能看出「这两行的值一样」，
        // 对低基数字段（性别、状态、是否吸毒）等同于直接泄露。
        var encryptor = CreateEncryptor(out _);

        var first = encryptor.Encrypt("same", "Demo.Tip.Body");
        var second = encryptor.Encrypt("same", "Demo.Tip.Body");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Decrypt_WithDifferentPurpose_Fails()
    {
        // 这是防密文重放的核心断言：把 A 列的密文搬到 B 列必须解不开。
        var encryptor = CreateEncryptor(out _);
        var cipher = encryptor.Encrypt("secret", "Demo.Tip.Body");

        var ex = Assert.Throws<FieldEncryptionException>(
            () => encryptor.Decrypt(cipher, "Demo.Tip.ReporterNote"));

        Assert.False(ex.IsKeyMissing);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Fails()
    {
        var encryptor = CreateEncryptor(out _);
        var cipher = encryptor.Encrypt("secret", "Demo.Tip.Body");

        // 翻掉载荷里的一个字符
        var tampered = cipher[..^2] + (cipher[^2] == 'A' ? 'B' : 'A') + cipher[^1];

        Assert.Throws<FieldEncryptionException>(() => encryptor.Decrypt(tampered, "Demo.Tip.Body"));
    }

    [Fact]
    public void Decrypt_AfterKeyRemovedFromRing_ReportsKeyMissing()
    {
        // 这正是「加密删除」的语义：销毁密钥后数据永久不可读，
        // 而且必须能与「密文损坏」区分开，否则运维无法判断是销毁还是被攻击。
        var encryptor = CreateEncryptor(out var options);
        var cipher = encryptor.Encrypt("to be destroyed", "Demo.Tip.Body");

        options.Keys.Clear();

        var ex = Assert.Throws<FieldEncryptionException>(
            () => encryptor.Decrypt(cipher, "Demo.Tip.Body"));

        Assert.True(ex.IsKeyMissing);
    }

    [Fact]
    public void KeyRotation_NewWritesUseNewKey_OldCiphertextStillReadable()
    {
        var encryptor = CreateEncryptor(out var options, KeyA, KeyB);
        var underOldKey = encryptor.Encrypt("written before rotation", "Demo.Tip.Body");

        // 轮换：把活动密钥指向新的一把，旧密钥留在环里
        options.ActiveKeyId = KeyB;
        var underNewKey = encryptor.Encrypt("written after rotation", "Demo.Tip.Body");

        Assert.StartsWith(KeyA + ":", underOldKey, StringComparison.Ordinal);
        Assert.StartsWith(KeyB + ":", underNewKey, StringComparison.Ordinal);
        Assert.Equal("written before rotation", encryptor.Decrypt(underOldKey, "Demo.Tip.Body"));
        Assert.Equal("written after rotation", encryptor.Decrypt(underNewKey, "Demo.Tip.Body"));
    }

    [Fact]
    public void Encrypt_WithoutActiveKey_Throws()
    {
        var encryptor = CreateEncryptor(out var options);
        options.ActiveKeyId = null;

        var ex = Assert.Throws<FieldEncryptionException>(
            () => encryptor.Encrypt("value", "Demo.Tip.Body"));

        Assert.True(ex.IsKeyMissing);
    }

    [Fact]
    public void IsEncrypted_DistinguishesCiphertextFromPlaintext()
    {
        // 供「给既有明文列加密」的迁移期使用：同一列会同时存在两种形态。
        var encryptor = CreateEncryptor(out _);
        var cipher = encryptor.Encrypt("value", "Demo.Tip.Body");

        Assert.True(encryptor.IsEncrypted(cipher));
        Assert.False(encryptor.IsEncrypted("plain text value"));
        Assert.False(encryptor.IsEncrypted("k1:not-really-protected"));
        Assert.False(encryptor.IsEncrypted(string.Empty));
    }

    [Fact]
    public void Validator_RejectsBadConfiguration()
    {
        var validator = new FieldEncryptionOptionsValidator();

        // 启用但没有密钥
        var noKeys = new FieldEncryptionOptions { Enabled = true };
        Assert.True(validator.Validate(null, noKeys).Failed);

        // 活动密钥不在环里
        var danglingActive = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = "missing",
            Keys = new Dictionary<string, string> { [KeyA] = NewKeyMaterial() }
        };
        Assert.True(validator.Validate(null, danglingActive).Failed);

        // 密钥长度不对（128 位而不是 256 位）
        var shortKey = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = KeyA,
            Keys = new Dictionary<string, string> { [KeyA] = Convert.ToBase64String(new byte[16]) }
        };
        Assert.True(validator.Validate(null, shortKey).Failed);

        // 密钥标识含冒号会破坏密文前缀的拆分
        var badId = new FieldEncryptionOptions
        {
            Enabled = true,
            ActiveKeyId = "a:b",
            Keys = new Dictionary<string, string> { ["a:b"] = NewKeyMaterial() }
        };
        Assert.True(validator.Validate(null, badId).Failed);
    }

    [Fact]
    public void Validator_SkipsKeyRingChecksWhenDisabled()
    {
        // 未启用时允许配置里留空壳，否则每个不用该能力的应用都得配一把假密钥。
        var validator = new FieldEncryptionOptionsValidator();

        var disabled = new FieldEncryptionOptions { Enabled = false };

        Assert.True(validator.Validate(null, disabled).Succeeded);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<FieldEncryptionOptions>
    {
        private readonly Func<FieldEncryptionOptions> _factory;

        public TestOptionsMonitor(Func<FieldEncryptionOptions> factory) => _factory = factory;

        public FieldEncryptionOptions CurrentValue => _factory();

        public FieldEncryptionOptions Get(string? name) => _factory();

        public IDisposable? OnChange(Action<FieldEncryptionOptions, string?> listener) => null;
    }
}
