using System.Security.Cryptography;

namespace Tnzi.Storage.Workspace.Tests;

/// <summary>
/// 分享口令哈希：慢哈希 + 版本化格式 + 认得出旧格式。
/// </summary>
/// <remarks>
/// 往返测试（Hash 再 Verify）看不出格式是不是真的换了 —— 两端一起退回单轮 HMAC 照样绿。
/// 所以这里另有一条<b>格式守卫</b>把线缆形态钉死，与 <c>SignaturePart_IsUnpaddedUrlSafeBase64</c> 同一理由。
/// </remarks>
public class SharePasswordHasherTests
{
    /// <summary>修复之前的写法：salt:hash，单轮 HMAC-SHA256。用它造存量记录。</summary>
    private static string LegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var hmac = new HMACSHA256(salt);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}".ToLowerInvariant();
    }

    [Fact]
    public void Hash_IsVersionedPbkdf2_WithTheIdentityIterationCount()
    {
        var stored = SharePasswordHasher.Hash("hunter2");

        var parts = stored.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2", parts[0]);
        Assert.Equal(SharePasswordHasher.Iterations.ToString(), parts[1]);
        Assert.Equal(100_000, SharePasswordHasher.Iterations);
        Assert.Equal(32, parts[2].Length);   // 16 字节盐
        Assert.Equal(64, parts[3].Length);   // 32 字节哈希
        Assert.DoesNotContain(':', stored);  // 不是旧格式
    }

    [Fact]
    public void Hash_SaltsEveryCall()
    {
        Assert.NotEqual(SharePasswordHasher.Hash("same"), SharePasswordHasher.Hash("same"));
    }

    [Fact]
    public void Verify_AcceptsTheRightPassword_AndRejectsTheWrongOne()
    {
        var stored = SharePasswordHasher.Hash("right");

        Assert.True(SharePasswordHasher.Verify("right", stored, out var rehash));
        Assert.False(rehash);
        Assert.False(SharePasswordHasher.Verify("wrong", stored, out _));
        Assert.False(SharePasswordHasher.Verify("", stored, out _));
    }

    /// <summary>存量链接的口令必须还能用 —— 而且要被标成「该升级」。</summary>
    [Fact]
    public void Verify_StillAcceptsTheLegacyHmacFormat_AndAsksForARehash()
    {
        var legacy = LegacyHash("old-secret");

        Assert.True(SharePasswordHasher.Verify("old-secret", legacy, out var rehash));
        Assert.True(rehash, "a legacy hash must be flagged for upgrade");
        Assert.False(SharePasswordHasher.Verify("not-it", legacy, out _));
    }

    /// <summary>迭代次数低于当前标准的记录也要升级：日后调高常量时自动生效。</summary>
    [Fact]
    public void Verify_FlagsAnOlderIterationCountForRehash()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2("pw", salt, 1000, HashAlgorithmName.SHA256, 32);
        var stored = $"pbkdf2$1000${Convert.ToHexString(salt)}${Convert.ToHexString(hash)}".ToLowerInvariant();

        Assert.True(SharePasswordHasher.Verify("pw", stored, out var rehash));
        Assert.True(rehash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2$notanumber$00$00")]
    [InlineData("pbkdf2$1000$zz$00")]
    [InlineData("zz:00")]
    [InlineData("a:b:c")]
    public void Verify_RejectsMalformedStoredHashes_WithoutThrowing(string stored)
    {
        Assert.False(SharePasswordHasher.Verify("pw", stored, out _));
    }
}
