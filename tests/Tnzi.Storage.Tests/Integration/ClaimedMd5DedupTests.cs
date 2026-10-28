namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// <c>GetOrCreateByMd5Async</c> 去重用的是流<b>算出来</b>的哈希，调用方声称的哈希只用来核对。
/// </summary>
/// <remarks>
/// 此前先按声称值去重、命中就返回，流的实际 MD5 到去重之后才算：附上一个不相干的流，就能拿到声称哈希对应的那条记录；
/// 记录在而对象丢了时还会把这个流按原键重传，等于把另一份字节写进那条记录，MD5 列却照旧。
/// </remarks>
public class ClaimedMd5DedupTests : StorageIntegrationTestBase
{
    private static readonly byte[] Original = "the contract everyone signed"u8.ToArray();
    private static readonly byte[] Forged = "something else entirely"u8.ToArray();
    private static readonly Guid Owner = Guid.Parse("0a0e0000-0000-0000-0000-000000000001");

    private void SignInAs(Guid userId)
    {
        var currentUser = Mock.Get(ServiceProvider.GetRequiredService<ICurrentUser>());
        currentUser.Setup(u => u.Id).Returns(userId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
    }

    private static string Md5Of(byte[] content) => Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

    private async Task<FileRecord> SaveOriginalAsync(FileStorageService service)
    {
        var created = await service.GetOrCreateByMd5Async(Md5Of(Original), "contract.txt", new MemoryStream(Original));
        Assert.True(created.Succeeded, created.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return created.Data!;
    }

    private async Task<FileRecord> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    private async Task<byte[]> ReadStoredAsync(FileRecord record)
    {
        await using var stream = await Storage.DownloadAsync(record.Path!);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>★★ 声称的哈希对不上流：400，不复用那条记录，计数一点不动。</summary>
    [Fact]
    public async Task AClaimThatDoesNotMatchTheBytes_IsRejected_WithoutTouchingTheClaimedRecord()
    {
        SignInAs(Owner);
        var service = CreateStorageService();
        var original = await SaveOriginalAsync(service);

        var result = await service.GetOrCreateByMd5Async(Md5Of(Original), "other.txt", new MemoryStream(Forged));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(1, (await ReloadAsync(original.Id)).ReferenceCount);
        Assert.Single(DbContext.FileRecords);
    }

    /// <summary>
    /// ★★★ 记录在而对象丢了的那一支：此前它把调用方的流按原键重传，别的字节就此住进了这条记录。
    /// </summary>
    [Fact]
    public async Task AMismatchedClaim_NeverRewritesTheBytesOfARecordWhoseObjectIsMissing()
    {
        SignInAs(Owner);
        var service = CreateStorageService();
        var original = await SaveOriginalAsync(service);
        await Storage.DeleteAsync(original.Path!);

        var result = await service.GetOrCreateByMd5Async(Md5Of(Original), "other.txt", new MemoryStream(Forged));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        var reloaded = await ReloadAsync(original.Id);
        Assert.False(await Storage.ExistsAsync(reloaded.Path!), "the forged bytes must not have been uploaded under the record's key");
    }

    /// <summary>对照：声称值与流一致（大小写不论）时照常按内容复用。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMatchingClaim_StillReusesTheRecord(bool upperCaseClaim)
    {
        SignInAs(Owner);
        var service = CreateStorageService();
        var original = await SaveOriginalAsync(service);
        var claim = upperCaseClaim ? Md5Of(Original).ToUpperInvariant() : Md5Of(Original);

        var result = await service.GetOrCreateByMd5Async(claim, "again.txt", new MemoryStream(Original));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original.Id, result.Data!.Id);
        await DbContext.SaveChangesAsync();
        Assert.Equal(2, (await ReloadAsync(original.Id)).ReferenceCount);
        Assert.Equal(Original, await ReadStoredAsync(original));
    }

    /// <summary>对照：没有声称值时按内容去重，与此前一致。</summary>
    [Fact]
    public async Task WithoutAClaim_DedupStillFollowsTheContent()
    {
        SignInAs(Owner);
        var service = CreateStorageService();
        var original = await SaveOriginalAsync(service);

        var result = await service.GetOrCreateByMd5Async(string.Empty, "again.txt", new MemoryStream(Original));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(original.Id, result.Data!.Id);
    }
}
