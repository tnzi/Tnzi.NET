using System.IO.Compression;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 按用户的存储配额：父模块的每一条把新字节交给 provider 的写路径都在交出之前判，超了答 413 / <c>FILE_QUOTA_EXCEEDED</c>，
/// 一条记录、一个对象都不留。
/// </summary>
/// <remarks>
/// 判据之所以要在服务层：控制器可以被消费方整体替换，而写路径有直传、批量、MD5 直存、复制、压缩、解压好几条，
/// 漏掉任何一条就是一个绕过配额的口子。每条路径一个用例，各自断言「拒绝 + 什么都没写下」。
/// </remarks>
public class StorageQuotaTests : StorageIntegrationTestBase
{
    private static readonly Guid Me = TestHelper.DefaultTestUserId;
    private static readonly Guid SomeoneElse = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private StorageOptions WithAllowance(long? bytes)
    {
        StorageOptions.Quota.DefaultUserAllowanceBytes = bytes;
        return StorageOptions;
    }

    private FileStorageService CreateQuotaAwareService(long? allowance, IStorageAllowanceProvider? provider = null)
    {
        var options = WithAllowance(allowance);
        return CreateStorageService(options: options, quota: CreateQuotaService(provider));
    }

    private async Task<int> RecordCountAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.CountAsync();
    }

    private static void AssertQuotaRefusal<T>(Tnzi.Results.Result<T> result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(413, result.Code);
        Assert.Equal(Tnzi.Exceptions.ErrorCodes.FILE_QUOTA_EXCEEDED, result.ErrorCode);
        Assert.IsType<StorageQuotaExceededDetails>(result.ErrorDetails);
    }

    [Fact]
    public async Task SaveAsync_RefusesAnUploadThatWouldExceedTheAllowance_AndWritesNothing()
    {
        await CreateStoredFileAsync("existing.txt", new byte[6]);
        var service = CreateQuotaAwareService(10);
        var before = await RecordCountAsync();

        var result = await service.SaveAsync("more.txt", new MemoryStream(new byte[5]));

        AssertQuotaRefusal(result);
        var details = (StorageQuotaExceededDetails)result.ErrorDetails!;
        Assert.Equal(6, details.UsedBytes);
        Assert.Equal(5, details.RequestedBytes);
        Assert.Equal(10, details.AllowanceBytes);
        Assert.Equal(before, await RecordCountAsync());
    }

    [Fact]
    public async Task SaveAsync_AcceptsAnUploadThatExactlyFills_TheAllowance()
    {
        await CreateStoredFileAsync("existing.txt", new byte[6]);
        var service = CreateQuotaAwareService(10);

        var result = await service.SaveAsync("fits.txt", new MemoryStream(new byte[4]));

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task NoAllowanceConfigured_MeansNoLimit()
    {
        await CreateStoredFileAsync("existing.txt", new byte[600]);
        var service = CreateQuotaAwareService(null);

        var result = await service.SaveAsync("big.txt", new MemoryStream(new byte[5000]));

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task ZeroAllowance_RefusesEveryNonEmptyUpload()
    {
        var service = CreateQuotaAwareService(0);

        AssertQuotaRefusal(await service.SaveAsync("one.txt", new MemoryStream(new byte[1])));
    }

    /// <summary>批里的记录在提交之前不落库，逐条判看不见前几条：一批两份各 6 字节在 10 字节的配额里要整批拒绝。</summary>
    [Fact]
    public async Task SaveManyAsync_JudgesTheWholeBatch()
    {
        var service = CreateQuotaAwareService(10);
        var before = await RecordCountAsync();

        var result = await service.SaveManyAsync(
        [
            ("a.txt", new MemoryStream(new byte[6])),
            ("b.txt", new MemoryStream(new byte[6]))
        ]);

        AssertQuotaRefusal(result);
        Assert.Equal(before, await RecordCountAsync());
    }

    [Fact]
    public async Task CopyAsync_CountsTheCopyAgainstTheCaller()
    {
        var source = await CreateStoredFileAsync("source.txt", new byte[6]);
        var service = CreateQuotaAwareService(10);
        var before = await RecordCountAsync();

        var result = await service.CopyAsync(source.Id);

        AssertQuotaRefusal(result);
        Assert.Equal(before, await RecordCountAsync());
    }

    [Fact]
    public async Task CompressAsync_CountsTheArchiveAgainstTheCaller()
    {
        var a = await CreateStoredFileAsync("a.txt", Enumerable.Range(0, 400).Select(i => (byte)(i * 7)).ToArray());
        var service = CreateQuotaAwareService(500);
        var before = await RecordCountAsync();

        var result = await service.CompressAsync([a.Id], "bundle");

        AssertQuotaRefusal(result);
        Assert.Equal(before, await RecordCountAsync());
    }

    /// <summary>解压超了配额作废整次解包（与总体积上限同一取舍）：一条记录都不落，已解出的对象删掉。</summary>
    [Fact]
    public async Task DecompressAsync_CountsTheExtractedBytes_AndAbandonsTheWholeArchive()
    {
        var zipBytes = BuildZip(("one.txt", new byte[40]), ("two.txt", new byte[40]));
        var archive = await CreateStoredFileAsync("bundle.zip", zipBytes);
        var service = CreateQuotaAwareService(zipBytes.Length + 60);
        var before = await RecordCountAsync();

        var result = await service.DecompressAsync(archive.Id);

        AssertQuotaRefusal(result);
        Assert.Equal(before, await RecordCountAsync());
    }

    [Fact]
    public async Task GetOrCreateByMd5Async_RefusesANewRecordOverTheAllowance()
    {
        await CreateStoredFileAsync("existing.txt", new byte[6]);
        var service = CreateQuotaAwareService(10);
        var content = Enumerable.Range(0, 5).Select(i => (byte)(i + 1)).ToArray();
        var md5 = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

        AssertQuotaRefusal(await service.GetOrCreateByMd5Async(md5, "new.txt", new MemoryStream(content)));
    }

    /// <summary>MD5 去重命中自己已有的记录不新增字节：配额已满时同一份字节再正式上传一次仍然成功。</summary>
    [Fact]
    public async Task ReusingMyOwnIdenticalBytes_IsNotChargedAgain()
    {
        var service = CreateQuotaAwareService(6);
        var content = Enumerable.Range(0, 6).Select(i => (byte)(i + 3)).ToArray();

        var first = await service.SaveAsync("a.txt", new MemoryStream(content));
        var second = await service.SaveAsync("a-again.txt", new MemoryStream(content));

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
    }

    [Fact]
    public async Task SaveWithoutDeduplication_GetsItsOwnRecordEveryTime()
    {
        var service = CreateStorageService();
        var content = Enumerable.Range(0, 8).Select(i => (byte)(i + 9)).ToArray();
        var options = new FileSaveOptions { Deduplicate = false };

        var first = await service.SaveAsync("a.txt", new MemoryStream(content), options);
        var second = await service.SaveAsync("a.txt", new MemoryStream(content), options);

        Assert.True(first.Succeeded && second.Succeeded);
        Assert.NotEqual(first.Data!.Id, second.Data!.Id);
        Assert.False(second.Data.IsTemporary);
        Assert.Equal(1, second.Data.ReferenceCount);
    }

    /// <summary>
    /// 系统身份代写：记录归指定的用户，配额也记在他名下 —— 而不是当前用户的（后台任务里根本没有当前用户）。
    /// </summary>
    [Fact]
    public async Task SaveForAnOwner_RecordsTheOwner_AndChargesTheirAllowance()
    {
        var allowances = new FixedAllowances { [SomeoneElse] = 3 };
        var service = CreateQuotaAwareService(null, allowances);

        var refused = await service.SaveAsync("theirs.txt", new MemoryStream(new byte[4]), new FileSaveOptions { OwnerId = SomeoneElse });
        var accepted = await service.SaveAsync("theirs.txt", new MemoryStream(new byte[3]), new FileSaveOptions { OwnerId = SomeoneElse });

        AssertQuotaRefusal(refused);
        Assert.True(accepted.Succeeded, accepted.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Equal(SomeoneElse, DbContext.FileRecords.Single(f => f.Id == accepted.Data!.Id).CreatorId);
    }

    [Fact]
    public async Task GetStorageQuotaAsync_ReportsUsedBytes_IncludingContributors_AndTheAllowance()
    {
        await CreateStoredFileAsync("a.txt", new byte[6]);
        await CreateStoredFileAsync("b.txt", new byte[4]);
        StorageOptions.Quota.DefaultUserAllowanceBytes = 100;
        var service = CreateStorageService(quota: CreateQuotaService(contributors: [new FixedUsage(25)]));

        var quota = (await service.GetStorageQuotaAsync(Me)).Data!;

        Assert.Equal(2, quota.FileCount);
        Assert.Equal(35, quota.UsedBytes);
        Assert.Equal(100, quota.AllowanceBytes);
        Assert.Equal(65, quota.RemainingBytes);
        Assert.False(quota.IsExceeded);
    }

    [Fact]
    public async Task GetMyStorageQuotaAsync_IsTheSignedInUsersOwn()
    {
        await CreateStoredFileAsync("a.txt", new byte[6]);
        var service = CreateStorageService(quota: CreateQuotaService());

        var quota = (await service.GetMyStorageQuotaAsync()).Data!;

        Assert.Equal(Me, quota.UserId);
        Assert.Equal(6, quota.UsedBytes);
        Assert.Null(quota.AllowanceBytes);
        Assert.Null(quota.RemainingBytes);
    }

    private static byte[] BuildZip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entryStream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                entryStream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private sealed class FixedAllowances : Dictionary<Guid, long?>, IStorageAllowanceProvider
    {
        public Task<long?> GetAllowanceAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(TryGetValue(userId, out var allowance) ? allowance : null);
    }

    private sealed class FixedUsage(long bytes) : IStorageUsageContributor
    {
        public Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(bytes);
    }
}
