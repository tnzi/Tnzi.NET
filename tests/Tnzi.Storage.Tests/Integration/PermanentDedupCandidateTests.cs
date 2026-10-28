using Tnzi.Results;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 正式上传按 MD5 复用时挑哪一条记录：只在正式记录里挑，挑法是确定的。
/// </summary>
/// <remarks>
/// <para>
/// 临时上传不参与去重之后，同一份 MD5 可以同时有正式记录 P 与若干条临时记录 T1..Tn。正式上传若落到某条
/// 临时记录上（此前还会把它转正），那条记录的所有者（临时上传者）对它有写权，却从没给它加过持有者：
/// 他取消草稿时的一次 <c>DeleteAsync</c> 扣掉的是正式上传者的那个 +1，计数归零当场物理删除，
/// 正式上传者从此指向一条不存在的记录，零症状。
/// </para>
/// <para>
/// 选哪条正式记录也必须确定：同一份 MD5 的正式记录可以有多条，数据库按堆顺序返回哪个不该决定结果。
/// </para>
/// </remarks>
public class PermanentDedupCandidateTests : StorageIntegrationTestBase
{
    private static readonly byte[] SharedBytes = "bytes held by a draft and by a signed copy"u8.ToArray();

    public static TheoryData<string> PermanentEntryPoints => new()
    {
        "SaveAsync",
        "SaveFromBytesAsync",
        "GetOrCreateByMd5Async",
        "SaveManyAsync",
    };

    private async Task<FileRecord> SavePermanentAsync(string entryPoint, string name, bool isPublic = false)
    {
        var service = CreateStorageService();
        Result<FileRecord> result;
        switch (entryPoint)
        {
            case "SaveAsync":
                result = await service.SaveAsync(name, new MemoryStream(SharedBytes), isTemporary: false, isPublic: isPublic);
                break;
            case "SaveFromBytesAsync":
                result = await service.SaveFromBytesAsync(name, SharedBytes, "text/plain", isPublic);
                break;
            case "GetOrCreateByMd5Async":
                result = await service.GetOrCreateByMd5Async(Md5Of(SharedBytes), name, new MemoryStream(SharedBytes));
                break;
            case "SaveManyAsync":
                var many = await service.SaveManyAsync([(name, new MemoryStream(SharedBytes))], isPublic);
                Assert.True(many.Succeeded, many.Message);
                result = Result.Success(many.Data!.Single());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null);
        }

        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    private async Task<FileRecord> SaveTemporaryAsync(string name)
    {
        var result = await CreateStorageService().SaveAsync(name, new MemoryStream(SharedBytes), isTemporary: true);
        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    private async Task DeleteAsync(Guid id)
    {
        var deleted = await CreateStorageService().DeleteAsync(id);
        Assert.True(deleted.Succeeded, deleted.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<FileRecord?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    }

    private static string Md5Of(byte[] content)
        => Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

    [Theory]
    [MemberData(nameof(PermanentEntryPoints))]
    public async Task PermanentUpload_NeverLandsOnSomeoneElsesDraft_SoTheDraftOwnerCannotDeleteItsBytes(string entryPoint)
    {
        // (1) A 把 X 存成草稿（临时上传）：自己的一条 T1。
        var original = await SavePermanentAsync("SaveAsync", "signed.txt");
        var draft = await SaveTemporaryAsync("draft.txt");
        Assert.NotEqual(original.Id, draft.Id);

        // (2) 正式记录 P 的持有者删掉了它：同一份 MD5 只剩 A 的草稿。
        await DeleteAsync(original.Id);
        Assert.Null(await ReloadAsync(original.Id));

        // (3) B 把同样的字节作为正式上传交进来：必须另存一份，不能拿到 A 的草稿。
        var permanent = await SavePermanentAsync(entryPoint, "final.txt");
        Assert.NotEqual(draft.Id, permanent.Id);
        Assert.False(permanent.IsTemporary);
        Assert.Equal(1, permanent.ReferenceCount);

        var untouchedDraft = await ReloadAsync(draft.Id);
        Assert.True(untouchedDraft!.IsTemporary);
        Assert.Equal(0, untouchedDraft.ReferenceCount);

        // (4) A 放弃草稿，以所有者身份删除它。
        await DeleteAsync(draft.Id);

        // (5) B 的文件还在，字节也还在。
        var survivor = await ReloadAsync(permanent.Id);
        Assert.NotNull(survivor);
        Assert.Equal(1, survivor!.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(survivor.Path!));
        var content = await CreateStorageService().GetAsync(permanent.Id);
        Assert.True(content.Succeeded, content.Message);
        await using var stream = content.Data!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(SharedBytes, copy.ToArray());
    }

    [Fact]
    public async Task PermanentUpload_PicksTheOldestPermanentRecord_WhateverOrderTheDatabaseReturns()
    {
        // 先插入较新的那条：不排序时 SQLite 按插入顺序返回，会先碰到它。
        var newer = await CreateStoredFileAsync("newer.txt", SharedBytes);
        var older = await CreateStoredFileAsync("older.txt", SharedBytes);
        await DbContext.FileRecords.Where(f => f.Id == newer.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-1)));
        await DbContext.FileRecords.Where(f => f.Id == older.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-2)));
        DbContext.ChangeTracker.Clear();

        var reused = await SavePermanentAsync("SaveAsync", "again.txt");

        Assert.Equal(older.Id, reused.Id);
        Assert.Equal(2, (await ReloadAsync(older.Id))!.ReferenceCount);
        Assert.Equal(1, (await ReloadAsync(newer.Id))!.ReferenceCount);
    }

    [Fact]
    public async Task PublicUpload_ReusesAPublicRecord_EvenWhenAnOlderPrivateOneExists()
    {
        // 较早的私密记录不能被公开上传复用（那等于替别人公开它）；较新的公开记录可以，不该因为
        // 「第一个命中的是私密的」就另存一份。
        var privateRecord = await CreateStoredFileAsync("private.txt", SharedBytes);
        var publicRecord = await CreateStoredFileAsync("public.txt", SharedBytes);
        await DbContext.FileRecords.Where(f => f.Id == privateRecord.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-2)));
        await DbContext.FileRecords.Where(f => f.Id == publicRecord.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-1))
                .SetProperty(f => f.IsPublic, true));
        DbContext.ChangeTracker.Clear();

        var reused = await SavePermanentAsync("SaveAsync", "avatar.txt", isPublic: true);

        Assert.Equal(publicRecord.Id, reused.Id);
        Assert.False((await ReloadAsync(privateRecord.Id))!.IsPublic);
        Assert.Equal(1, (await ReloadAsync(privateRecord.Id))!.ReferenceCount);
    }
}
