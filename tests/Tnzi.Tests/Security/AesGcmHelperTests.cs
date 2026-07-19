using System.Security.Cryptography;
using Tnzi.Security;

namespace Tnzi.Tests.Security;

/// <summary>
/// AesGcmHelper 单元测试:往返、版本前缀、随机 nonce、篡改检测、密钥校验
/// </summary>
public class AesGcmHelperTests
{
    private static byte[] NewKey() => Convert.FromBase64String(AesGcmHelper.GenerateKeyBase64());

    [Fact]
    public void EncryptDecrypt_RoundTrip_PreservesPlaintext()
    {
        var key = NewKey();
        const string plaintext = "0123456789 银行账号 with unicode ✓";

        var protectedValue = AesGcmHelper.Encrypt(plaintext, key);

        Assert.StartsWith("v1:", protectedValue, StringComparison.Ordinal);
        Assert.Equal(plaintext, AesGcmHelper.Decrypt(protectedValue, key));
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertext()
    {
        var key = NewKey();

        var first = AesGcmHelper.Encrypt("same", key);
        var second = AesGcmHelper.Encrypt("same", key);

        // 随机 nonce 保证语义安全:同明文两次加密密文不同
        Assert.NotEqual(first, second);
        Assert.Equal("same", AesGcmHelper.Decrypt(first, key));
        Assert.Equal("same", AesGcmHelper.Decrypt(second, key));
    }

    [Fact]
    public void Encrypt_EmptyString_RoundTrips()
    {
        var key = NewKey();
        var protectedValue = AesGcmHelper.Encrypt(string.Empty, key);
        Assert.Equal(string.Empty, AesGcmHelper.Decrypt(protectedValue, key));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var key = NewKey();
        var protectedValue = AesGcmHelper.Encrypt("sensitive", key);

        var raw = Convert.FromBase64String(protectedValue["v1:".Length..]);
        raw[^1] ^= 0xFF; // 翻转 tag 末字节
        var tampered = "v1:" + Convert.ToBase64String(raw);

        Assert.ThrowsAny<CryptographicException>(() => AesGcmHelper.Decrypt(tampered, key));
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var protectedValue = AesGcmHelper.Encrypt("sensitive", NewKey());
        Assert.ThrowsAny<CryptographicException>(() => AesGcmHelper.Decrypt(protectedValue, NewKey()));
    }

    [Fact]
    public void Decrypt_MissingVersionPrefix_ThrowsFormat()
    {
        Assert.Throws<FormatException>(() => AesGcmHelper.Decrypt("bm90LXByZWZpeGVk", NewKey()));
    }

    [Fact]
    public void Decrypt_TooShortPayload_ThrowsFormat()
    {
        var tooShort = "v1:" + Convert.ToBase64String(new byte[10]);
        Assert.Throws<FormatException>(() => AesGcmHelper.Decrypt(tooShort, NewKey()));
    }

    [Fact]
    public void InvalidKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => AesGcmHelper.Encrypt("x", new byte[16]));
        Assert.Throws<ArgumentException>(() => AesGcmHelper.Decrypt("v1:xxxx", new byte[31]));
    }

    [Fact]
    public void IsProtected_DetectsPrefix()
    {
        Assert.True(AesGcmHelper.IsProtected("v1:abc"));
        Assert.True(AesGcmHelper.IsProtected("v2:abc"));
        Assert.False(AesGcmHelper.IsProtected("plain"));
        Assert.False(AesGcmHelper.IsProtected(null));
        Assert.False(AesGcmHelper.IsProtected(string.Empty));
    }

    [Fact]
    public void EncryptWithAad_RoundTrips_WithSameAad_AndUsesV2Prefix()
    {
        var key = NewKey();
        var aad = System.Text.Encoding.UTF8.GetBytes("owner:123");
        var protectedValue = AesGcmHelper.Encrypt("account-number", key, aad);

        Assert.StartsWith("v2:", protectedValue);
        Assert.Equal("account-number", AesGcmHelper.Decrypt(protectedValue, key, aad));
    }

    [Fact]
    public void DecryptWithWrongAad_Throws()
    {
        var key = NewKey();
        var protectedValue = AesGcmHelper.Encrypt("account-number", key, System.Text.Encoding.UTF8.GetBytes("owner:A"));

        // 用另一归属键（另一条记录）解密 → 认证失败，密文不可被搬移复用
        Assert.ThrowsAny<CryptographicException>(
            () => AesGcmHelper.Decrypt(protectedValue, key, System.Text.Encoding.UTF8.GetBytes("owner:B")));
        // 完全不给 AAD 也失败
        Assert.ThrowsAny<CryptographicException>(() => AesGcmHelper.Decrypt(protectedValue, key));
    }

    [Fact]
    public void V1Ciphertext_DecryptsIgnoringAad_BackwardCompatible()
    {
        var key = NewKey();
        var v1 = AesGcmHelper.Encrypt("legacy", key); // 无 AAD (v1)
        Assert.StartsWith("v1:", v1);

        // 存量 v1 密文即便传入 AAD 也照常解密（忽略 AAD）
        Assert.Equal("legacy", AesGcmHelper.Decrypt(v1, key, System.Text.Encoding.UTF8.GetBytes("whatever")));
    }
}
