namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 匿名访客视角的分享链接 —— 也就是这个功能存在的全部理由。
///
/// 单独一个测试类是因为集成基类默认注册的 <c>ICurrentUser</c> 恒为「已登录」。
/// 这里最后再注册一次覆盖掉它,才能真正走到"没有账号的人点开链接"那条路;
/// 拿已登录用户去测匿名开关,测的其实是另一件事。
/// </summary>
public class ShareLinkAnonymousTests : StorageIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 后注册者胜出:把基类那个"已登录"的当前用户换成匿名访客。
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
    }

    /// <summary>
    /// 上传要绕开当前用户(匿名上传本来就该被挡),所以直接建记录 + 落盘。
    /// </summary>
    private async Task<FileRecord> SeedFileAsync(string name = "quote.txt")
    {
        var bytes = Encoding.UTF8.GetBytes($"bytes of {name}");
        var path = await Storage.UploadAsync(name, new MemoryStream(bytes), "text/plain");
        var record = new FileRecord
        {
            FileName = name,
            OriginalName = name,
            Extension = ".txt",
            ContentType = "text/plain",
            Size = bytes.Length,
            Path = path,
            Provider = "Local",
        };
        await DbContext.Set<FileRecord>().AddAsync(record);
        await DbContext.SaveChangesAsync();
        return record;
    }

    private async Task<Tnzi.Storage.Entities.FileShare> SeedShareAsync(Guid fileId, string? password = null)
    {
        // 创建分享要写权限,匿名当然没有 —— 这里用全放行的授权器代表"某个有权的人造了这条链接"。
        var share = (await CreateShareService().CreateShareAsync(fileId, password: password)).Data;
        Assert.NotNull(share);
        return share!;
    }

    [Fact]
    public async Task AnAnonymousVisitor_CanOpenAndReadTheLink()
    {
        var file = await SeedFileAsync();
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        var share = await SeedShareAsync(file.Id);

        // 收件人先看到"这是什么文件"。
        var preview = await shares.GetSharePreviewAsync(share.ShareToken);
        Assert.True(preview.Succeeded);
        Assert.Equal("quote.txt", preview.Data!.FileName);

        // 再凭同一个令牌取到字节 —— 全程没有任何登录身份。
        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken)).Data);

        var reader = CreateStorageService(grantContext: grants);
        Assert.True((await reader.GetRecordAsync(file.Id)).Succeeded);
    }

    [Fact]
    public async Task WithAnonymousSharingOff_TheVisitorIsTurnedAway()
    {
        var file = await SeedFileAsync();
        var share = await SeedShareAsync(file.Id);
        StorageOptions.Share.AllowAnonymous = false;
        var shares = CreateShareService();

        Assert.False((await shares.ValidateShareAccessAsync(share.ShareToken)).Data);
        Assert.False((await shares.GetSharePreviewAsync(share.ShareToken)).Succeeded);
    }

    [Fact]
    public async Task WithoutTheRightPassword_TheVisitorGetsNothing()
    {
        var file = await SeedFileAsync();
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        var share = await SeedShareAsync(file.Id, password: "letmein");

        Assert.False((await shares.ValidateShareAccessAsync(share.ShareToken)).Data);
        Assert.False((await shares.ValidateShareAccessAsync(share.ShareToken, "nope")).Data);

        var reader = CreateStorageService(grantContext: grants);
        Assert.False((await reader.GetRecordAsync(file.Id)).Succeeded);

        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "letmein")).Data);
        Assert.True((await reader.GetRecordAsync(file.Id)).Succeeded);
    }

    [Fact]
    public async Task TheLinkNeverBecomesAWriteCredential()
    {
        // 收件人是外部的人。他能取到这一个文件,不该能删它、改它,或换一张不受
        // 次数与过期约束的访问令牌。
        var file = await SeedFileAsync();
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        var share = await SeedShareAsync(file.Id);
        await shares.ValidateShareAccessAsync(share.ShareToken);

        var authorizer = CreateRealAuthorizer(grants);
        Assert.True(await authorizer.CanReadAsync(file));
        Assert.False(await authorizer.CanWriteAsync(file));
        Assert.False(await authorizer.CanMintAccessTokenAsync(file));

        var reader = CreateStorageService(grantContext: grants);
        Assert.False((await reader.DeleteAsync(file.Id)).Succeeded);
        Assert.False((await reader.CreateAccessTokenAsync(file.Id)).Succeeded);
    }
}
