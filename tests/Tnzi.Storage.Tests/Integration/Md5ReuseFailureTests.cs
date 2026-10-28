namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// MD5 复用先原子 +1 占住命中的记录，再看对象在不在。占住之后失败了，这一次 +1 要还回去。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：+1 改成原子语句之后，它在没有外层工作单元时当场提交。随后的存在性检查（云 provider 在权限、超时、
/// 网络故障时抛出）或重传一旦抛出，上传以错误结束，而那次 +1 留在了库里、没有任何人持有它：
/// 这条记录从此要多删一次才删得掉，孤儿回收也永远收不走它。此前计数是在内存里加、成功之后才落库，失败不留痕。
/// </para>
/// </remarks>
public class Md5ReuseFailureTests : StorageIntegrationTestBase
{
    private static readonly byte[] Bytes = "quarterly report, identical bytes uploaded twice"u8.ToArray();
    private static readonly Guid Owner = Guid.Parse("0a0e0000-0000-0000-0000-000000000001");

    private void SignInAs(Guid userId)
    {
        var currentUser = Mock.Get(ServiceProvider.GetRequiredService<ICurrentUser>());
        currentUser.Setup(u => u.Id).Returns(userId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
    }

    private async Task<int> PersistedCountAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().Where(f => f.Id == id).Select(f => f.ReferenceCount).SingleAsync();
    }

    [Fact]
    public async Task AReuseWhoseExistenceCheckThrows_GivesItsHoldBack()
    {
        SignInAs(Owner);
        var first = await CreateStorageService().SaveAsync("report.txt", new MemoryStream(Bytes), isTemporary: false);
        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(1, await PersistedCountAsync(first.Data!.Id));

        var failing = new ExistsThrowingStorage(Storage);
        await Assert.ThrowsAsync<IOException>(
            () => CreateStorageService(failing).SaveAsync("report-again.txt", new MemoryStream(Bytes), isTemporary: false));

        Assert.True(failing.ExistsCalled, "the upload never reached the reuse path");
        Assert.Equal(1, await PersistedCountAsync(first.Data.Id));
    }

    /// <summary>对照：存在性检查正常时，复用照常多加一个持有者。</summary>
    [Fact]
    public async Task ASuccessfulReuse_KeepsItsHold()
    {
        SignInAs(Owner);
        var service = CreateStorageService();
        var first = await service.SaveAsync("report.txt", new MemoryStream(Bytes), isTemporary: false);
        var second = await service.SaveAsync("report-again.txt", new MemoryStream(Bytes), isTemporary: false);

        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.Equal(2, await PersistedCountAsync(first.Data.Id));
    }

    /// <summary>存在性检查抛错（云 provider 在权限、超时、网络故障时就是这样），其余照常转给真实的 provider。</summary>
    private sealed class ExistsThrowingStorage(IFileStorage inner) : IFileStorage
    {
        public bool ExistsCalled { get; private set; }

        public string ProviderName => inner.ProviderName;

        public Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null) => inner.UploadAsync(fileName, stream, contentType);

        public Task<Stream> DownloadAsync(string filePath) => inner.DownloadAsync(filePath);

        public Task<bool> DeleteAsync(string filePath) => inner.DeleteAsync(filePath);

        public Task<bool> ExistsAsync(string filePath)
        {
            ExistsCalled = true;
            throw new IOException("The storage endpoint did not answer.");
        }

        public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => inner.GetUrlAsync(filePath, expiresIn);

        public Task<long> GetFileSizeAsync(string filePath) => inner.GetFileSizeAsync(filePath);

        public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
            string filePath, long? rangeStart = null, long? rangeEnd = null)
            => inner.DownloadRangeAsync(filePath, rangeStart, rangeEnd);

        public Task<string?> CopyAsync(string sourcePath, string destFileName) => inner.CopyAsync(sourcePath, destFileName);
    }
}
