namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// T4：过期分片上传会话 + 残留分片（含物理文件）的清理。
/// </summary>
/// <remarks>
/// 这一趟原先写死在父模块的 <c>FileCleanupService</c> 里，直接吃
/// <c>IRepository&lt;FileUploadSession&gt;</c> 与 <c>IRepository&lt;FileChunk&gt;</c> 两个仓储。
/// 两张表随本模块搬走之后，它改由 <see cref="ExpiredUploadSessionCleanupContributor"/> 挂进去 ——
/// 断言的行为一字未改，变的只是它由谁驱动。
/// </remarks>
public class ExpiredUploadSessionCleanupTests : WorkspaceIntegrationTestBase
{
    // ------------------------------------------------------------------
    // T4: 过期分片上传会话 + 残留分片清理
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExpiredSessions_DeletesExpiredSessionAndChunks()
    {
        var (session, chunkPaths) = await SeedUploadSessionWithChunksAsync(null, expired: true);

        // 物理文件确实存在
        foreach (var path in chunkPaths)
        {
            Assert.True(await Storage.ExistsAsync(path), $"chunk file should exist before cleanup: {path}");
        }

        var contributor = CreateCleanupContributor();

        var deleted = await contributor.CleanupAsync(maxItems: 100);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await DbContext.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == session.Id));
        Assert.Equal(0, await DbContext.FileChunks.IgnoreQueryFilters().CountAsync(c => c.UploadSessionId == session.Id));

        // 物理分片文件也被删除
        foreach (var path in chunkPaths)
        {
            Assert.False(await Storage.ExistsAsync(path), $"chunk file should be deleted: {path}");
        }
    }

    [Fact]
    public async Task ExpiredSessions_KeepsNonExpiredSession()
    {
        var (session, _) = await SeedUploadSessionWithChunksAsync(null, expired: false);

        var contributor = CreateCleanupContributor();

        var deleted = await contributor.CleanupAsync(maxItems: 100);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await DbContext.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == session.Id));
        Assert.Equal(2, await DbContext.FileChunks.IgnoreQueryFilters().CountAsync(c => c.UploadSessionId == session.Id));
    }

    [Fact]
    public void Contributor_IsDiscoverableByName()
    {
        // 名字进日志，是这一趟在运维那里唯一的自证方式。
        Assert.Equal("ExpiredUploadSessions", CreateCleanupContributor().Name);
    }

    [Fact]
    public async Task ExpiredSessions_RespectsMaxItems()
    {
        await SeedUploadSessionWithChunksAsync(null, expired: true);
        await SeedUploadSessionWithChunksAsync(null, expired: true);

        var deleted = await CreateCleanupContributor().CleanupAsync(maxItems: 1);

        // 单趟上限由调用方（父模块的 Storage:Cleanup:MaxFilesPerRun）给，不由本类自己定。
        Assert.Equal(1, deleted);
        Assert.Equal(1, await DbContext.FileUploadSessions.IgnoreQueryFilters().CountAsync());
    }

    private async Task<(FileUploadSession session, List<string> chunkPaths)> SeedUploadSessionWithChunksAsync(Guid? tenantId, bool expired)
    {
        var session = new FileUploadSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = "big.zip",
            TotalSize = 6,
            ChunkSize = 3,
            TotalChunks = 2,
            CreationTime = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(24)
        };
        DbContext.FileUploadSessions.Add(session);

        var paths = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"c{i}x"));
            var path = await Storage.UploadAsync($"chunk_{session.Id}_{i}.part", stream, "application/octet-stream");
            paths.Add(path);
            DbContext.FileChunks.Add(new FileChunk
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UploadSessionId = session.Id,
                ChunkIndex = i,
                ChunkSize = 3,
                ChunkPath = path,
                CreationTime = DateTime.UtcNow.AddHours(-2)
            });
        }

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (session, paths);
    }
}
