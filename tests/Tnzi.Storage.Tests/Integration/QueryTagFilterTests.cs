namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// <see cref="FileQueryRequest.Tag"/> 是整项匹配（契约写的就是 exact match），与 <c>GetFilesByTagAsync</c> 同一个判据。
/// </summary>
/// <remarks>
/// 此前查询按子串匹配：按 <c>voice</c> 过滤会混进所有带 <c>invoice</c> 标签的文件，按 <c>a</c> 过滤几乎返回所有带标签的文件。
/// </remarks>
public class QueryTagFilterTests : StorageIntegrationTestBase
{
    [Fact]
    public async Task TheTagFilter_MatchesWholeTagsOnly()
    {
        await CreateStoredFileAsync("memo.txt", [1], tags: "voice,draft");
        await CreateStoredFileAsync("bill.txt", [2], tags: "invoice");
        await CreateStoredFileAsync("plain.txt", [3]);
        DbContext.ChangeTracker.Clear();

        var result = await CreateStorageService().QueryFilesAsync(new FileQueryRequest { Tag = "voice" });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("memo.txt", Assert.Single(result.Data!.Items).OriginalName);
        Assert.Equal(1, result.Data.TotalCount);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("  draft ")]
    public async Task TheTagFilter_IgnoresCaseAndSurroundingWhitespace(string tag)
    {
        await CreateStoredFileAsync("memo.txt", [1], tags: "voice,draft");
        DbContext.ChangeTracker.Clear();

        var result = await CreateStorageService().QueryFilesAsync(new FileQueryRequest { Tag = tag });

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Data!.Items);
    }

    [Fact]
    public async Task ASingleLetter_DoesNotMatchEveryTaggedFile()
    {
        await CreateStoredFileAsync("memo.txt", [1], tags: "voice,draft");
        await CreateStoredFileAsync("bill.txt", [2], tags: "invoice");
        DbContext.ChangeTracker.Clear();

        var result = await CreateStorageService().QueryFilesAsync(new FileQueryRequest { Tag = "a" });

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(result.Data!.Items);
    }
}
