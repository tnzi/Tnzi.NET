namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 工作区里的两条写路径受同一份配额约束：分片上传（建会话按声明的大小拦一次，完成时按实际字节再判）与建新版本
/// （旧版本的字节留着，新版本又是一份）。历史版本的字节计入用量，按用户的覆盖值优先于默认值。
/// </summary>
public class WorkspaceQuotaTests : FileSpaceTestBase
{
    private static byte[] Bytes(int count) => Enumerable.Range(0, count).Select(i => (byte)(i % 251)).ToArray();

    [Fact]
    public async Task ChunkedUpload_IsRefusedUpFront_WhenTheDeclaredSizeDoesNotFit()
    {
        StorageOptions.Quota.DefaultUserAllowanceBytes = 10;
        var service = CreateChunkUploadService(quota: CreateQuotaService());

        var init = await service.InitiateChunkedUploadAsync("big.txt", 11, 4);

        Assert.Equal(413, init.Code);
        Assert.Equal(Tnzi.Exceptions.ErrorCodes.FILE_QUOTA_EXCEEDED, init.ErrorCode);
        DbContext.ChangeTracker.Clear();
        Assert.False(await DbContext.FileUploadSessions.AnyAsync());
    }

    /// <summary>会话建立之后别的上传用掉了空间：完成时按实际字节再判，拒绝时分片原样保留，腾出空间后可以再 complete。</summary>
    [Fact]
    public async Task ChunkedUpload_IsJudgedAgainAtCompletion()
    {
        StorageOptions.Quota.DefaultUserAllowanceBytes = 10;
        var service = CreateChunkUploadService(quota: CreateQuotaService());
        var session = (await service.InitiateChunkedUploadAsync("late.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream(Bytes(3)));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream(Bytes(3)));
        await UploadAsync("meanwhile.txt", "12345");

        var completed = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.Equal(413, completed.Code);
        DbContext.ChangeTracker.Clear();
        Assert.False(await DbContext.FileRecords.AnyAsync(f => f.OriginalName == "late.txt"));
        Assert.Equal(2, await DbContext.FileChunks.CountAsync(c => c.UploadSessionId == session.Id));
    }

    [Fact]
    public async Task ChunkedUpload_CanLandStraightInAFolder()
    {
        var docs = await CreateFolderAsync("Docs");
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("video.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream(Bytes(3)));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream(Bytes(3)));

        var completed = await service.CompleteChunkedUploadAsync(session.Id, isTemporary: false, folderId: docs.Id);

        Assert.True(completed.Succeeded, completed.Message);
        Assert.Equal(docs.Id, completed.Data!.FolderId);
        Assert.Equal(["video.txt"], Names(await ListAsync(docs.Id)));
    }

    [Fact]
    public async Task ChunkedUpload_IntoSomeoneElsesFolder_OrAsATemporaryFiledFile_IsRefusedBeforeMerging()
    {
        var theirs = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Theirs")).Data!;
        var mine = await CreateFolderAsync("Mine");
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("x.txt", 3, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream(Bytes(3)));

        Assert.Equal(404, (await service.CompleteChunkedUploadAsync(session.Id, false, theirs.Id)).Code);
        Assert.Equal(400, (await service.CompleteChunkedUploadAsync(session.Id, true, mine.Id)).Code);
        Assert.True((await service.CompleteChunkedUploadAsync(session.Id, false, mine.Id)).Succeeded);
    }

    [Fact]
    public async Task OldVersions_CountTowardsTheOwnersUsage()
    {
        var file = await UploadAsync("doc.txt", "12345");
        Assert.True((await CreateVersionService().CreateVersionAsync(file.Id, new MemoryStream(Bytes(7)))).Succeeded);

        // 当前版本 7 字节（记录的大小）+ 被替换下来的 v1 5 字节。
        Assert.Equal(12, await CreateQuotaService().GetUsedBytesAsync(Me));
    }

    /// <summary>按安装 / 按用户计费的存储用量同样算上历史版本（文件记录的字节由存储模块自己算，这里只报版本那一份）。</summary>
    [Fact]
    public async Task OldVersions_CountTowardsTheInstallationsBillableUsage_Too()
    {
        var file = await UploadAsync("doc.txt", "12345");
        Assert.True((await CreateVersionService().CreateVersionAsync(file.Id, new MemoryStream(Bytes(7)))).Succeeded);
        var contributor = new FileVersionUsageContributor(Repo<FileVersion>(), Repo<FileRecord>());

        Assert.Equal(5, await contributor.GetUsedBytesAsync(StorageUsageScope.Installation));
        Assert.Equal(5, await contributor.GetUsedBytesAsync(StorageUsageScope.ForUser(Me)));
        Assert.Equal(0, await contributor.GetUsedBytesAsync(StorageUsageScope.ForUser(Guid.NewGuid())));
    }

    /// <summary>超了配额的新版本：一行版本记录都不写（不会留下一条凭空多出的「Initial version」），对象也删掉。</summary>
    [Fact]
    public async Task ANewVersionOverTheQuota_IsRefused_AndLeavesNoVersionRows()
    {
        var file = await UploadAsync("doc.txt", "12345");
        StorageOptions.Quota.DefaultUserAllowanceBytes = 9;
        var service = CreateVersionService(quota: CreateQuotaService());

        var result = await service.CreateVersionAsync(file.Id, new MemoryStream(Bytes(5)));

        Assert.Equal(413, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.False(await DbContext.FileVersions.AnyAsync());
        Assert.Equal(5, (await ReloadFileAsync(file.Id))!.Size);
    }

    [Fact]
    public async Task APerUserAllowance_OverridesTheDefault_AndRemovingItRestoresTheDefault()
    {
        StorageOptions.Quota.DefaultUserAllowanceBytes = 1000;
        var provider = new StorageAllowanceProvider(Repo<StorageAllowance>(), new StaticOptionsMonitor<StorageOptions>(StorageOptions));
        var allowances = CreateAllowanceService();

        Assert.Equal(1000, await provider.GetAllowanceAsync(Me));

        var set = await allowances.SetAsync(Me, new SetStorageAllowanceRequest { AllowanceBytes = 5, Note = "trial" });
        Assert.True(set.Succeeded, set.Message);
        Assert.Equal(5, await provider.GetAllowanceAsync(Me));

        await allowances.SetAsync(Me, new SetStorageAllowanceRequest { AllowanceBytes = null });
        Assert.Null(await provider.GetAllowanceAsync(Me));

        await allowances.RemoveAsync(Me);
        Assert.Equal(1000, await provider.GetAllowanceAsync(Me));
        Assert.Equal(404, (await allowances.GetAsync(Me)).Code);
    }

    [Fact]
    public async Task APerUserAllowance_IsWhatTheUploadPathsEnforce()
    {
        StorageOptions.Quota.DefaultUserAllowanceBytes = null;
        await CreateAllowanceService().SetAsync(Me, new SetStorageAllowanceRequest { AllowanceBytes = 4 });
        var service = CreateSpaceService(CreateStorageService(quota: CreateQuotaService()));

        var result = await service.UploadFileAsync(null, "five.txt", new MemoryStream(Bytes(5)));

        Assert.Equal(413, result.Code);
        Assert.Equal(4, ((StorageQuotaExceededDetails)result.ErrorDetails!).AllowanceBytes);
    }

    [Fact]
    public async Task TheAllowanceList_ShowsOnlyUsersWithAnOverride()
    {
        await CreateAllowanceService().SetAsync(SomeoneElse, new SetStorageAllowanceRequest { AllowanceBytes = 2048 });

        var list = (await CreateAllowanceService().GetListAsync(new StorageAllowanceQueryDto())).Data!;

        var only = Assert.Single(list.Items);
        Assert.Equal(SomeoneElse, only.UserId);
        Assert.Equal(2048, only.AllowanceBytes);
    }

    [Fact]
    public async Task ANegativeAllowance_IsRefused()
    {
        var result = await CreateAllowanceService().SetAsync(Me, new SetStorageAllowanceRequest { AllowanceBytes = -1 });

        Assert.Equal(400, result.Code);
    }
}
