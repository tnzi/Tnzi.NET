using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 存储键生成器：键的形态就是「服务端生成」这条不变量在字符串层面的样子。
/// </summary>
public class StorageKeyHelperTests
{
    [Theory]
    [InlineData(".pdf", ".pdf")]
    [InlineData(".PDF", ".pdf")]
    [InlineData(".tar", ".tar")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NewKey_IsASequentialGuidPlusTheExtension(string? extension, string expectedSuffix)
    {
        var key = StorageKeyHelper.NewKey(extension);

        Assert.True(StorageKeyHelper.IsGenerated(key), key);
        Assert.EndsWith(expectedSuffix, key);
        Assert.True(Guid.TryParse(key[..36], out _), "the leading 36 characters must be a GUID");
    }

    /// <summary>
    /// 扩展名不是可信输入：它来自 <c>Path.GetExtension(调用方给的名字)</c>。
    /// 不合形态的直接不带，而不是让一次保存失败 —— 权威扩展名在 FileRecord.Extension。
    /// </summary>
    [Theory]
    [InlineData(".pdf/")]
    [InlineData(".p df")]
    [InlineData(".exe\\..")]
    [InlineData("pdf")]
    [InlineData(".a-very-long-extension-that-nobody-uses")]
    public void NewKey_DropsAnExtensionThatIsNotPlainAlphanumerics(string extension)
    {
        var key = StorageKeyHelper.NewKey(extension);

        Assert.Equal(36, key.Length);
        Assert.True(StorageKeyHelper.IsGenerated(key));
    }

    [Fact]
    public void NewKey_NeverRepeats()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => StorageKeyHelper.NewKey(".txt")).ToHashSet();
        Assert.Equal(1000, keys.Count);
    }

    [Fact]
    public void ThumbnailAndChunkKeys_AreRecognisedAsGenerated()
    {
        var key = StorageKeyHelper.NewKey(".png");
        Assert.True(StorageKeyHelper.IsGenerated(StorageKeyHelper.ThumbnailKey(key)));
        Assert.True(StorageKeyHelper.IsGenerated(StorageKeyHelper.ChunkKey(Guid.NewGuid(), 7)));
    }

    /// <summary>
    /// 这些正是三条漂移过的写路径当初拿来当键的东西。它们一个都不该被认成「生成的」，
    /// 否则 <c>StorageKeyInvariantTests</c> 的断言就是恒真。
    /// </summary>
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("bundle.zip")]
    [InlineData("their-contract.pdf")]
    [InlineData("../../etc/passwd")]
    [InlineData("2026/01/01/report.pdf")]
    [InlineData("thumb_report.pdf")]
    [InlineData("chunk_not-a-guid_0")]
    [InlineData("")]
    [InlineData(null)]
    public void IsGenerated_RejectsCallerSuppliedNames(string? key)
    {
        Assert.False(StorageKeyHelper.IsGenerated(key));
    }

    /// <summary>形态检查不接受「GUID + 任意后缀」：后缀里带路径分隔符的键不是本类生成的。</summary>
    [Fact]
    public void IsGenerated_RejectsAGuidFollowedByAPath()
    {
        Assert.False(StorageKeyHelper.IsGenerated($"{Guid.NewGuid()}/../x.txt"));
        Assert.False(StorageKeyHelper.IsGenerated($"{Guid.NewGuid()}.tar.gz"));
    }
}
