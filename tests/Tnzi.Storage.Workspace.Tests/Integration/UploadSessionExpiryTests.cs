namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 分片上传会话的有效期要在<b>写路径</b>上生效，不能只靠后台清理。
/// </summary>
/// <remarks>
/// <para>
/// <c>InitiateChunkedUploadAsync</c> 给每个会话写 24 小时的 <c>ExpiresAt</c>，可此前 <c>UploadChunkAsync</c> 与
/// <c>CompleteChunkedUploadAsync</c> 都只看 <c>IsCompleted</c> / <c>IsCancelled</c> / 归属 —— 一个 30 天前开的会话
/// 照样能收分块、能合并落库，直到受 <c>Storage:Cleanup:MaxFilesPerRun</c> 限速的清理任务轮到它。
/// 有效期于是只是一个展示字段。
/// </para>
/// <para>
/// 另一条：清理任务先删物理分片再删会话行，中间失败会留下「行在、分片不在」的会话；对它调用 complete
/// 此前在 <c>DownloadAsync</c> 处抛 <c>FileNotFoundException</c>（500），而不是一个能让客户端重来的 400。
/// </para>
/// </remarks>
public class UploadSessionExpiryTests : WorkspaceIntegrationTestBase
{
    [Fact]
    public async Task AnExpiredSession_RefusesNewChunks()
    {
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("late.txt", 6, 3)).Data!;
        await ExpireAsync(session.Id);

        var result = await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(DbContext.FileChunks.Where(c => c.UploadSessionId == session.Id));
    }

    [Fact]
    public async Task AnExpiredSession_CannotBeCompleted()
    {
        // 所有分块都到了、只是超时了：过期会话不该再产出一条文件记录。
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("late.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));
        await ExpireAsync(session.Id);

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(DbContext.FileRecords);
    }

    [Fact]
    public async Task AnExpiredSession_CanStillBeCancelled()
    {
        // 取消是收尾动作：对过期会话放行它，客户端就能自己把残留分片清掉，不用等后台任务。
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("late.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        var chunkPath = DbContext.FileChunks.Single(c => c.UploadSessionId == session.Id).ChunkPath!;
        await ExpireAsync(session.Id);

        var result = await service.CancelChunkedUploadAsync(session.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(DbContext.FileUploadSessions.AsNoTracking().Single(s => s.Id == session.Id).IsCancelled);
        Assert.False(await Storage.ExistsAsync(chunkPath));
    }

    [Fact]
    public async Task AFreshSession_IsUnaffected()
    {
        // 对照：有效期内的会话逐字维持原行为。
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("ok.txt", 6, 3)).Data!;
        Assert.True((await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()))).Succeeded);
        Assert.True((await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()))).Succeeded);

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(DbContext.FileRecords);
    }

    [Fact]
    public async Task CompletingASessionWhoseChunksAreGone_AnswersABadRequest_NotAnException()
    {
        // 清理任务或对象存储把分片弄丢了，行还在：客户端要得到一个能据以重传的 400，
        // 而不是从 provider 冒出来的 FileNotFoundException。
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("gone.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        var lost = DbContext.FileChunks.Single(c => c.UploadSessionId == session.Id && c.ChunkIndex == 1);
        await Storage.DeleteAsync(lost.ChunkPath!);

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("1", result.Message, StringComparison.Ordinal);
        Assert.Empty(DbContext.FileRecords);
        // 会话没有被标成完成：客户端重传那一片之后还能再试。
        Assert.False(DbContext.FileUploadSessions.AsNoTracking().Single(s => s.Id == session.Id).IsCompleted);
    }

    /// <summary>把会话的有效期拨到过去 —— 数据库里就是这么一个过期会话，与放了 30 天的一样。</summary>
    private async Task ExpireAsync(Guid sessionId)
    {
        var tracked = DbContext.FileUploadSessions.Single(s => s.Id == sessionId);
        tracked.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await DbContext.SaveChangesAsync();
    }
}
