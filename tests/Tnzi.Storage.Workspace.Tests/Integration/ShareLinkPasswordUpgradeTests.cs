using System.Security.Cryptography;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 存量分享链接（单轮 HMAC 口令哈希）在第一次验证通过时就地升级成 PBKDF2。
/// </summary>
/// <remarks>
/// 升级只能发生在「手里有明文口令」的那一刻，也就是收件人输对口令的那次请求；
/// 后台批量重算是做不到的（库里只有哈希）。所以这条路径要么在这里，要么永远不会发生。
/// </remarks>
public class ShareLinkPasswordUpgradeTests : WorkspaceIntegrationTestBase
{
    private static string LegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var hmac = new HMACSHA256(salt);
        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)))}".ToLowerInvariant();
    }

    private async Task<FileShare> SeedLegacyShareAsync(string password)
    {
        var file = await CreateStoredFileAsync("legacy.txt", "bytes"u8.ToArray());
        var share = new FileShare
        {
            FileId = file.Id,
            ShareToken = Guid.NewGuid().ToString("N"),
            RequirePassword = true,
            PasswordHash = LegacyHash(password),
            IsEnabled = true,
        };
        DbContext.FileShares.Add(share);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return share;
    }

    private async Task<string?> StoredHashAsync(Guid shareId)
        => (await DbContext.FileShares.AsNoTracking().SingleAsync(s => s.Id == shareId)).PasswordHash;

    [Fact]
    public async Task ARightPassword_OnALegacyHash_StillOpensTheLink_AndUpgradesTheStoredHash()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        var shares = CreateShareService();

        var opened = await shares.ValidateShareAccessAsync(share.ShareToken, "old-secret");

        Assert.True(opened.Data);
        var stored = await StoredHashAsync(share.Id);
        Assert.StartsWith("pbkdf2$", stored);
        Assert.True(SharePasswordHasher.Verify("old-secret", stored!, out var rehash));
        Assert.False(rehash, "once upgraded the hash is current");
    }

    /// <summary>升级是一次性的：第二次验证不再改写（写一次是升级，每次都写是无谓的 I/O）。</summary>
    [Fact]
    public async Task TheUpgradeHappensOnce()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        var shares = CreateShareService();

        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "old-secret")).Data);
        var afterFirst = await StoredHashAsync(share.Id);
        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "old-secret")).Data);
        var afterSecond = await StoredHashAsync(share.Id);

        Assert.Equal(afterFirst, afterSecond);
    }

    /// <summary>错口令既不放行也不升级 —— 升级不能成为不带口令改写哈希的旁路。</summary>
    [Fact]
    public async Task AWrongPassword_OnALegacyHash_NeitherOpensNorUpgrades()
    {
        var share = await SeedLegacyShareAsync("old-secret");
        var shares = CreateShareService();

        var opened = await shares.ValidateShareAccessAsync(share.ShareToken, "nope");

        Assert.False(opened.Data);
        var stored = await StoredHashAsync(share.Id);
        Assert.Contains(':', stored!);
        Assert.DoesNotContain("pbkdf2", stored);
    }

    [Fact]
    public async Task ANewShare_StoresAPbkdf2Hash()
    {
        var file = await CreateStoredFileAsync("fresh.txt", "bytes"u8.ToArray());
        var created = (await CreateShareService().CreateShareAsync(file.Id, password: "s3cret")).Data!;

        var stored = await StoredHashAsync(created.Id);
        Assert.StartsWith("pbkdf2$", stored);
    }
}
