namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 对外分享链接：令牌本身就是凭据，收件人往往根本没有账号。
///
/// 这些用例守的是那条线：**校验通过的分享让本次请求读得到那个文件，而且仅此而已** ——
/// 不给写权限、不能换成访问令牌、不能跨文件、口令错了要有代价。
/// </summary>
public class ShareLinkTests : StorageIntegrationTestBase
{
    // 扩展名要在基类的白名单里；内容按文件名区分，否则 MD5 去重会把两次上传合成一条记录。
    private async Task<FileRecord> UploadPrivateAsync(string name = "contract.txt")
    {
        var result = await CreateStorageService()
            .SaveAsync(name, new MemoryStream(Encoding.UTF8.GetBytes($"secret bytes of {name}")));
        Assert.True(result.Succeeded, result.Message);
        return result.Data!;
    }

    // ── 授予：分享令牌让本次请求读得到 ────────────────────────────────────────

    [Fact]
    public async Task AValidatedShare_LetsThisRequestReadTheFile()
    {
        // 关键点：reader 用的是**真实**授权器 + 匿名调用者 —— 它本来什么都不放行，
        // 放行完全来自分享校验写进请求作用域的授予。没有这条，分享链接就只对"本来就
        // 读得到这个文件的人"有效，也就是没用。
        var file = await UploadPrivateAsync();
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        var share = (await shares.CreateShareAsync(file.Id)).Data!;

        var reader = CreateStorageService(grantContext: grants);
        Assert.False((await reader.GetRecordAsync(file.Id)).Succeeded);

        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken)).Data);

        Assert.True((await reader.GetRecordAsync(file.Id)).Succeeded);

        // 取到的是真流,要关掉 —— 否则临时目录清理会撞"文件被占用"。
        var stream = await reader.GetAsync(file.Id);
        Assert.True(stream.Succeeded);
        await stream.Data!.DisposeAsync();
    }

    [Fact]
    public async Task AFailedValidation_GrantsNothing()
    {
        var file = await UploadPrivateAsync();
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        Assert.True((await shares.CreateShareAsync(file.Id)).Succeeded);

        Assert.False((await shares.ValidateShareAccessAsync("not-a-real-token")).Data);

        var reader = CreateStorageService(grantContext: grants);
        Assert.False((await reader.GetRecordAsync(file.Id)).Succeeded);
    }

    [Fact]
    public async Task AShare_GrantsOnlyItsOwnFile()
    {
        var shared = await UploadPrivateAsync("shared.txt");
        var other = await UploadPrivateAsync("other.txt");
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        var share = (await shares.CreateShareAsync(shared.Id)).Data!;

        await shares.ValidateShareAccessAsync(share.ShareToken);

        var reader = CreateStorageService(grantContext: grants);
        Assert.True((await reader.GetRecordAsync(shared.Id)).Succeeded);
        Assert.False((await reader.GetRecordAsync(other.Id)).Succeeded);
    }

    // ── 授予的边界：读，且仅仅是读 ──────────────────────────────────────────

    [Fact]
    public async Task AShareGrant_DoesNotAllowWritesOrTokenMinting()
    {
        // 分享链接是限次数、会过期的。若它能换一张访问令牌，那些约束就被绕开了；
        // 若它能删文件，收件人就成了文件的主人。
        var file = await UploadPrivateAsync();
        var grants = new FileAccessGrantContext();
        var shares = CreateShareService(grants);
        var share = (await shares.CreateShareAsync(file.Id)).Data!;
        await shares.ValidateShareAccessAsync(share.ShareToken);

        var authorizer = CreateRealAuthorizer(grants);
        Assert.True(await authorizer.CanReadAsync(file));
        Assert.False(await authorizer.CanWriteAsync(file));
        Assert.False(await authorizer.CanMintAccessTokenAsync(file));
    }

    // ── 部署策略 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithAnonymousSharingOff_ASignedInColleagueStillGetsIn()
    {
        // 关掉匿名不是"作废这条链接",而是把它降级成内部传阅 —— 已登录的人照常能用。
        var file = await UploadPrivateAsync();
        StorageOptions.Share.AllowAnonymous = false;
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id)).Data!;

        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken)).Data);
        Assert.True((await shares.GetSharePreviewAsync(share.ShareToken)).Succeeded);
    }

    [Fact]
    public async Task WithPasswordEnforced_ACreatorCannotOptOut()
    {
        var file = await UploadPrivateAsync();
        StorageOptions.Share.RequirePassword = true;
        var shares = CreateShareService();

        var withoutPassword = await shares.CreateShareAsync(file.Id);
        Assert.False(withoutPassword.Succeeded);
        Assert.Equal(400, withoutPassword.Code);

        Assert.True((await shares.CreateShareAsync(file.Id, password: "s3cret")).Succeeded);
    }

    [Fact]
    public async Task AnUnspecifiedExpiry_GetsTheConfiguredDefault()
    {
        // 永不过期的链接没有人会记得回来撤销，它会一直躺在某封邮件里。
        var file = await UploadPrivateAsync();
        StorageOptions.Share.DefaultExpiryDays = 7;
        var share = (await CreateShareService().CreateShareAsync(file.Id)).Data!;

        Assert.NotNull(share.ExpiresAt);
        Assert.InRange(share.ExpiresAt!.Value, DateTime.UtcNow.AddDays(6), DateTime.UtcNow.AddDays(8));
    }

    [Fact]
    public async Task AnOverlongExpiry_IsClampedRatherThanRejected()
    {
        // 创建分享的人多半只是随手挑了个远日期，为此报错只会让他重试一遍。
        var file = await UploadPrivateAsync();
        StorageOptions.Share.MaxExpiryDays = 30;
        var share = (await CreateShareService().CreateShareAsync(file.Id, expiresAt: DateTime.UtcNow.AddYears(5))).Data!;

        Assert.True(share.ExpiresAt < DateTime.UtcNow.AddDays(31));
    }

    [Fact]
    public async Task AShorterExpiry_IsLeftAlone()
    {
        var file = await UploadPrivateAsync();
        StorageOptions.Share.MaxExpiryDays = 30;
        var requested = DateTime.UtcNow.AddDays(2);

        var share = (await CreateShareService().CreateShareAsync(file.Id, expiresAt: requested)).Data!;

        Assert.Equal(requested, share.ExpiresAt!.Value, TimeSpan.FromSeconds(1));
    }

    // ── 口令爆破 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RepeatedWrongPasswords_DisableTheLink()
    {
        // 令牌是 256 位随机数猜不到，但口令可以在线爆破。
        var file = await UploadPrivateAsync();
        StorageOptions.Share.MaxFailedPasswordAttempts = 3;
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id, password: "right")).Data!;

        for (var i = 0; i < 3; i++)
        {
            Assert.False((await shares.ValidateShareAccessAsync(share.ShareToken, "wrong")).Data);
        }

        // 闸门落下之后，连正确口令也不再放行 —— 链接已经泄漏，重发一条才是正解。
        Assert.False((await shares.ValidateShareAccessAsync(share.ShareToken, "right")).Data);
    }

    [Fact]
    public async Task TheFailureCounter_IsPersistedOutsideTheRequestTransaction()
    {
        // ★这条守的是一个真实踩过的坑：输错口令的请求以 401 收场，而启用了
        // EnableGlobalUnitOfWork 的部署会把失败请求的整个事务回滚 —— 计数于是永远
        // 写不进去，闸门形同虚设（实测过：连错 10 次后正确口令照样放行）。
        // 所以落库必须走独立 DI 作用域。这里断言它确实用了那条路。
        var file = await UploadPrivateAsync();
        StorageOptions.Share.MaxFailedPasswordAttempts = 5;
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id, password: "right")).Data!;

        await shares.ValidateShareAccessAsync(share.ShareToken, "wrong");

        // 从**另一个**作用域回读：拿到的必须是已经落库的值，而不是本作用域的脏跟踪。
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Tnzi.Storage.Entities.FileShare, Guid>>();
        var persisted = await repo.GetAsync(share.Id);
        Assert.Equal(1, persisted!.FailedAttemptCount);
    }

    [Fact]
    public async Task ASuccessfulUnlock_ResetsTheFailureCounter()
    {
        var file = await UploadPrivateAsync();
        StorageOptions.Share.MaxFailedPasswordAttempts = 3;
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id, password: "right")).Data!;

        await shares.ValidateShareAccessAsync(share.ShareToken, "wrong");
        await shares.ValidateShareAccessAsync(share.ShareToken, "wrong");
        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "right")).Data);

        // 计数已清零，所以还能再错两次而不被锁。
        await shares.ValidateShareAccessAsync(share.ShareToken, "wrong");
        await shares.ValidateShareAccessAsync(share.ShareToken, "wrong");
        Assert.True((await shares.ValidateShareAccessAsync(share.ShareToken, "right")).Data);
    }

    // ── 收件人预览 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ThePreview_TellsTheRecipientWhatTheyAreAboutToOpen()
    {
        var file = await UploadPrivateAsync("quote.txt");
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id, password: "pw")).Data!;

        var preview = (await shares.GetSharePreviewAsync(share.ShareToken)).Data!;

        Assert.Equal("quote.txt", preview.FileName);
        Assert.Equal(file.Size, preview.Size);
        // 口令只把住取字节那一关：收件人得先知道这里要口令。
        Assert.True(preview.RequirePassword);
    }

    [Fact]
    public async Task ARevokedOrExpiredLink_PreviewsAsNotFound()
    {
        // 撤销 / 过期 / 次数用尽与"令牌根本不存在"折叠成同一个 404 ——
        // 区分开就等于告诉试探者哪些令牌是真的。
        var file = await UploadPrivateAsync();
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id)).Data!;

        await shares.RevokeShareAsync(share.ShareToken);

        var preview = await shares.GetSharePreviewAsync(share.ShareToken);
        Assert.False(preview.Succeeded);
        Assert.Equal(404, preview.Code);
        Assert.Equal(404, (await shares.GetSharePreviewAsync("never-existed")).Code);
    }

    [Fact]
    public async Task UsingALink_StampsWhenItWasLastUsed()
    {
        var file = await UploadPrivateAsync();
        var shares = CreateShareService();
        var share = (await shares.CreateShareAsync(file.Id)).Data!;
        Assert.Null(share.LastAccessedAt);

        Assert.True((await shares.IncrementShareAccessCountAsync(share.ShareToken)).Data);

        var reloaded = (await shares.GetShareAsync(share.ShareToken)).Data!;
        Assert.NotNull(reloaded.LastAccessedAt);
    }
}
