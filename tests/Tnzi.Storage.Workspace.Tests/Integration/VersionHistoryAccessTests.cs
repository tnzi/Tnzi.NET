namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 历史版本只对「本来就读得了这个文件」的人开放，请求级凭据（<c>?sig=</c> 渲染令牌、分享授予）不算。
/// </summary>
/// <remarks>
/// 请求级凭据授予的是<b>当前这份内容</b>的一次读取。版本读取此前问的是读取判据，于是一条发出去给人看当前版本、
/// 10 分钟过期的链接能列出并取回每一个旧版本，包括已经被替换掉、脱敏之前的内容。
/// </remarks>
public class VersionHistoryAccessTests : WorkspaceIntegrationTestBase
{
    private async Task<FileRecord> CreateVersionedFileAsync()
    {
        var file = await CreateStoredFileAsync("contract.txt", "v1 with the unredacted clause"u8.ToArray());
        var created = await CreateVersionService().CreateVersionAsync(file.Id, new MemoryStream("v2 redacted"u8.ToArray()));
        Assert.True(created.Succeeded, created.Message);
        DbContext.ChangeTracker.Clear();
        return file;
    }

    [Fact]
    public async Task ARequestLevelCredential_DoesNotListTheHistory()
    {
        var file = await CreateVersionedFileAsync();

        var result = await CreateVersionService(authorizer: TestFileAccessAuthorizer.ReadableButNotMintable())
            .GetVersionsAsync(file.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task ARequestLevelCredential_DoesNotDownloadAnOldVersion()
    {
        var file = await CreateVersionedFileAsync();

        var result = await CreateVersionService(authorizer: TestFileAccessAuthorizer.ReadableButNotMintable())
            .GetVersionContentAsync(file.Id, 1);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task ACallerWhoMayReadTheFileForGood_StillSeesTheHistory()
    {
        // 正控制：所有者 / 权限码 / 引用判据照常能看历史（它们都在签发判据里）。
        var file = await CreateVersionedFileAsync();
        var service = CreateVersionService(authorizer: TestFileAccessAuthorizer.AllowAll());

        var versions = await service.GetVersionsAsync(file.Id);
        Assert.True(versions.Succeeded, versions.Message);
        Assert.Equal(2, versions.Data!.Count());

        var content = await service.GetVersionContentAsync(file.Id, 1);
        Assert.True(content.Succeeded, content.Message);
        await using var stream = content.Data!;
        using var reader = new StreamReader(stream);
        Assert.Equal("v1 with the unredacted clause", await reader.ReadToEndAsync());
    }
}
