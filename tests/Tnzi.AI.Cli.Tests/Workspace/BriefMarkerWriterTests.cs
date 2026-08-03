namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 受管 brief 写入与回滚。
/// </summary>
/// <remarks>
/// 这组测试守的是一条<b>用户数据安全</b>性质，而不是格式细节：
/// <c>WorkDirectoryMode = UserProvided</c> 时工作目录就是用户自己的仓库，
/// 那里的 <c>CLAUDE.md</c> / <c>AGENTS.md</c> 可能是他写了很久的东西。
/// 参考实现早期在这一步无条件覆写，把用户的文件整个截断了。
/// </remarks>
public class BriefMarkerWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "tnzi-cli-brief-" + Guid.NewGuid().ToString("N"));

    public BriefMarkerWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不该让测试红。
        }

        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task Write_WhenFileMissing_CreatesFileWithOnlyTheManagedBlock()
    {
        var path = Path_("CLAUDE.md");

        var created = await BriefMarkerWriter.WriteAsync(path, "hello", CancellationToken.None);

        created.ShouldBeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.ShouldStartWith(BriefMarkerWriter.MarkerBegin);
        content.ShouldContain("hello");
        content.ShouldEndWith(BriefMarkerWriter.MarkerEnd + "\n");
    }

    [Fact]
    public async Task Cleanup_WhenWeCreatedTheFile_RemovesItEntirely()
    {
        var path = Path_("CLAUDE.md");
        var created = await BriefMarkerWriter.WriteAsync(path, "hello", CancellationToken.None);

        await BriefMarkerWriter.CleanupAsync(path, created, CancellationToken.None);

        // 目录列表必须回到写入前的样子，否则用户会在 git status 里看到一个陌生文件。
        File.Exists(path).ShouldBeFalse();
    }

    [Theory]
    // 三种尾部形态各测一次：它们正是"顺手归一化"最容易改坏的地方。
    [InlineData("user content without trailing newline")]
    [InlineData("user content with one newline\n")]
    [InlineData("user content with three newlines\n\n\n")]
    [InlineData("")]
    public async Task WriteThenCleanup_RestoresUserFileByteForByte(string original)
    {
        var path = Path_("AGENTS.md");
        await File.WriteAllTextAsync(path, original);

        var created = await BriefMarkerWriter.WriteAsync(path, "managed brief", CancellationToken.None);
        created.ShouldBeFalse();

        var afterWrite = await File.ReadAllTextAsync(path);
        afterWrite.ShouldStartWith(original);
        afterWrite.ShouldContain("managed brief");

        await BriefMarkerWriter.CleanupAsync(path, created, CancellationToken.None);

        var restored = await File.ReadAllTextAsync(path);
        restored.ShouldBe(original);
    }

    [Fact]
    public async Task Write_Repeatedly_ReplacesInPlaceInsteadOfAppending()
    {
        var path = Path_("CLAUDE.md");
        await File.WriteAllTextAsync(path, "user content\n");

        await BriefMarkerWriter.WriteAsync(path, "first", CancellationToken.None);
        await BriefMarkerWriter.WriteAsync(path, "second", CancellationToken.None);
        await BriefMarkerWriter.WriteAsync(path, "third", CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);

        // 同一工作目录反复运行不能让文件无限增长。
        CountOccurrences(content, BriefMarkerWriter.MarkerBegin).ShouldBe(1);
        content.ShouldContain("third");
        content.ShouldNotContain("first");
        content.ShouldNotContain("second");
        content.ShouldStartWith("user content\n");
    }

    [Fact]
    public async Task Write_WhenUserContentContainsStrayEndMarker_StillReplacesInPlace()
    {
        // 用户文档里展示这个格式（"我们的 CI 会往文件里插一段这样的块"）时会出现孤立的
        // 结束标记。朴素的双 IndexOf 会判定"没有块"，于是每次运行再追加一块，文件无限增长。
        var path = Path_("CLAUDE.md");
        await File.WriteAllTextAsync(path, $"docs mention {BriefMarkerWriter.MarkerEnd} here\n");

        await BriefMarkerWriter.WriteAsync(path, "first", CancellationToken.None);
        await BriefMarkerWriter.WriteAsync(path, "second", CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        CountOccurrences(content, BriefMarkerWriter.MarkerBegin).ShouldBe(1);
        content.ShouldContain("second");
        content.ShouldNotContain("first");
    }

    [Fact]
    public async Task Write_AfterCrashLeftHalfABlock_ReplacesTheHalfBlock()
    {
        // 上一次运行在写起始标记之后崩溃。把「有起始、无结束」当成"块延伸到文件末尾"，
        // 下一次写入才能原地替换而不是在半块下面再叠一块。
        var path = Path_("CLAUDE.md");
        await File.WriteAllTextAsync(path, $"user\n\n{BriefMarkerWriter.MarkerBegin}\nhalf written");

        await BriefMarkerWriter.WriteAsync(path, "recovered", CancellationToken.None);

        var content = await File.ReadAllTextAsync(path);
        CountOccurrences(content, BriefMarkerWriter.MarkerBegin).ShouldBe(1);
        content.ShouldContain("recovered");
        content.ShouldNotContain("half written");
    }

    [Fact]
    public async Task Cleanup_WhenNoManagedBlockPresent_LeavesFileUntouched()
    {
        var path = Path_("AGENTS.md");
        await File.WriteAllTextAsync(path, "purely user content");

        await BriefMarkerWriter.CleanupAsync(path, createdByUs: false, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).ShouldBe("purely user content");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
