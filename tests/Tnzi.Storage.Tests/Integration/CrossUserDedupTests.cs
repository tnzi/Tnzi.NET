using Tnzi.Results;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 正式上传按 MD5 复用时，候选只来自<b>调用者自己</b>的记录。
/// </summary>
/// <remarks>
/// <para>
/// 复用的是整条 <see cref="FileRecord"/>，不只是底层字节：记录的 <c>CreatorId</c>、原始文件名、标签、目录都跟着它走。
/// 跨用户命中时，上传者拿回的是别人的那条记录，响应里就是对方的文件名与 id（「有人持有完全相同的内容」的确认）；
/// 上传者不是这条记录的所有者，下载、删除自己刚传的文件都是 404；而原所有者仍对它有写权，建版本、改可见性、
/// 反复删除都作用在上传者以为是自己的那份文件上。
/// </para>
/// <para>
/// 没有当前用户（后台任务、匿名请求）时不去重：<c>CreatorId</c> 为 null 的记录不归任何人所有，
/// 两次无主保存之间没有「同一个人」可言。
/// </para>
/// </remarks>
public class CrossUserDedupTests : StorageIntegrationTestBase
{
    private static readonly byte[] SharedBytes = "salary sheet, identical bytes in two accounts"u8.ToArray();
    private static readonly Guid Alice = Guid.Parse("a11ce000-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("b0b00000-0000-0000-0000-000000000002");

    public static TheoryData<string> PermanentEntryPoints => new()
    {
        "SaveAsync",
        "SaveFromBytesAsync",
        "GetOrCreateByMd5Async",
        "SaveManyAsync",
    };

    private void SignInAs(Guid? userId)
    {
        var currentUser = Mock.Get(ServiceProvider.GetRequiredService<ICurrentUser>());
        currentUser.Setup(u => u.Id).Returns(userId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(userId.HasValue);
    }

    private async Task<FileRecord> SavePermanentAsync(string entryPoint, string name)
    {
        var service = CreateStorageService();
        Result<FileRecord> result;
        switch (entryPoint)
        {
            case "SaveAsync":
                result = await service.SaveAsync(name, new MemoryStream(SharedBytes), isTemporary: false);
                break;
            case "SaveFromBytesAsync":
                result = await service.SaveFromBytesAsync(name, SharedBytes, "text/plain");
                break;
            case "GetOrCreateByMd5Async":
                result = await service.GetOrCreateByMd5Async(Md5Of(SharedBytes), name, new MemoryStream(SharedBytes));
                break;
            case "SaveManyAsync":
                var many = await service.SaveManyAsync([(name, new MemoryStream(SharedBytes))]);
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

    private async Task<FileRecord?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    }

    private static string Md5Of(byte[] content)
        => Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

    [Theory]
    [MemberData(nameof(PermanentEntryPoints))]
    public async Task AnotherUsersRecord_IsNeverHandedToTheUploader(string entryPoint)
    {
        SignInAs(Alice);
        var alices = await SavePermanentAsync(entryPoint, "2026-salary-alice.txt");

        SignInAs(Bob);
        var bobs = await SavePermanentAsync(entryPoint, "x.txt");

        // Bob 拿到的是他自己的一条记录：他的名字、他是所有者，不是 Alice 的文件名与 id。
        Assert.NotEqual(alices.Id, bobs.Id);
        Assert.Equal(Bob, bobs.CreatorId);
        Assert.Equal("x.txt", bobs.OriginalName);

        // Alice 的记录没有多出一个她不知道的持有者。
        var alicesNow = await ReloadAsync(alices.Id);
        Assert.Equal(1, alicesNow!.ReferenceCount);
        Assert.Equal("2026-salary-alice.txt", alicesNow.OriginalName);
    }

    [Theory]
    [MemberData(nameof(PermanentEntryPoints))]
    public async Task TheSameUser_StillReusesTheirOwnRecord(string entryPoint)
    {
        // 正控制：去重本身没有被关掉，只是限定在调用者自己的记录里。
        SignInAs(Alice);
        var first = await SavePermanentAsync(entryPoint, "report.txt");
        var second = await SavePermanentAsync(entryPoint, "report-again.txt");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, (await ReloadAsync(first.Id))!.ReferenceCount);
    }

    [Fact]
    public async Task WithoutACurrentUser_NothingIsReused()
    {
        // 后台任务 / 匿名请求存下的记录 CreatorId 为 null，不归任何人所有：两次无主保存不共享一条记录。
        SignInAs(null);
        var first = await SavePermanentAsync("SaveAsync", "job-output.txt");
        var second = await SavePermanentAsync("SaveAsync", "job-output.txt");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(1, (await ReloadAsync(first.Id))!.ReferenceCount);
        Assert.Equal(1, (await ReloadAsync(second.Id))!.ReferenceCount);
    }

    [Fact]
    public async Task AnonymousUpload_DoesNotLandOnAUsersRecord()
    {
        SignInAs(Alice);
        var alices = await SavePermanentAsync("SaveAsync", "private.txt");

        SignInAs(null);
        var anonymous = await SavePermanentAsync("SaveAsync", "public-form.txt");

        Assert.NotEqual(alices.Id, anonymous.Id);
        Assert.Equal(1, (await ReloadAsync(alices.Id))!.ReferenceCount);
    }
}
