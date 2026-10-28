namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 受管 brief 的注入与回滚按<b>字节</b>往返，而不是按文本往返。
/// </summary>
/// <remarks>
/// ★ 此前读写都走 <c>ReadAllText</c> / <c>WriteAllText</c>：读时剥掉 BOM、把 UTF-16 解码、把非法 UTF-8 字节换成 U+FFFD，
/// 写时恒为无 BOM 的 UTF-8。<c>UserProvided</c> 指向用户仓库时，一个 Windows 编辑器存的带 BOM 的 <c>AGENTS.md</c>
/// 每跑一次就少 3 个字节（git status 出现改动），UTF-16 文件被整体转成 UTF-8，GBK / Shift-JIS 等文件里的非 ASCII 字节
/// 被永久替换成 EF BF BD，用户内容不可恢复。原有的回滚测试用 <c>ReadAllText</c> 比较全 ASCII 的字符串，测不到这一层。
/// 这里一律按字节比较。
/// </remarks>
public class BriefMarkerWriterEncodingTests : IDisposable
{
    private readonly string _directory = TestScratch.NewPath("brief-enc");

    public BriefMarkerWriterEncodingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        TestScratch.Delete(_directory);

        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    private const string UserText = "# 项目约定\r\n\r\n请用中文回答。Keep commits small.\r\n";

    public static TheoryData<string, byte[]> UnicodeFiles() => new()
    {
        { "utf-8 with bom, crlf, cjk", [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(UserText)] },
        { "utf-8 without bom, cjk", new UTF8Encoding(false).GetBytes(UserText) },
        { "utf-16 le with bom", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(UserText)] },
        { "utf-16 be with bom", [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(UserText)] },
        { "utf-32 le with bom", [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes(UserText)] },
        { "ascii without trailing newline", Encoding.ASCII.GetBytes("plain user content") },
        { "empty", [] }
    };

    [Theory]
    [MemberData(nameof(UnicodeFiles))]
    public async Task WriteThenCleanup_RestoresTheUserFileByteForByte(string _, byte[] original)
    {
        var path = Path_("AGENTS.md");
        await File.WriteAllBytesAsync(path, original);

        var created = await BriefMarkerWriter.WriteAsync(LocalHostFileSystem.Instance, path, "managed brief 受管", CancellationToken.None);
        created.ShouldBeFalse();
        var afterWrite = await File.ReadAllBytesAsync(path);
        afterWrite.ShouldNotBe(original);

        await BriefMarkerWriter.CleanupAsync(LocalHostFileSystem.Instance, path, created, CancellationToken.None);

        (await File.ReadAllBytesAsync(path)).ShouldBe(original);
    }

    [Theory]
    [MemberData(nameof(UnicodeFiles))]
    public async Task Write_KeepsTheFilesOwnEncodingAndByteOrderMark(string _, byte[] original)
    {
        var path = Path_("AGENTS.md");
        await File.WriteAllBytesAsync(path, original);

        await BriefMarkerWriter.WriteAsync(LocalHostFileSystem.Instance, path, "managed brief 受管", CancellationToken.None);

        // 用户原有的字节（含 BOM）一个不差地留在最前面，受管块按同一编码追加，CLI 读得出来
        var afterWrite = await File.ReadAllBytesAsync(path);
        afterWrite.AsSpan(0, original.Length).SequenceEqual(original).ShouldBeTrue();
        using var reader = new StreamReader(new MemoryStream(afterWrite), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();
        text.ShouldContain(BriefMarkerWriter.MarkerBegin);
        text.ShouldContain("managed brief 受管");
    }

    [Fact]
    public async Task Write_ReplacingAnExistingBlock_KeepsEveryByteOutsideIt()
    {
        var path = Path_("AGENTS.md");
        byte[] original = [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(UserText)];
        await File.WriteAllBytesAsync(path, original);

        var created = await BriefMarkerWriter.WriteAsync(LocalHostFileSystem.Instance, path, "first", CancellationToken.None);
        await BriefMarkerWriter.WriteAsync(LocalHostFileSystem.Instance, path, "second", CancellationToken.None);
        await BriefMarkerWriter.CleanupAsync(LocalHostFileSystem.Instance, path, created, CancellationToken.None);

        (await File.ReadAllBytesAsync(path)).ShouldBe(original);
    }

    [Fact]
    public async Task Write_LegacyEncodedFile_IsRefused_AndLeftByteForByteUntouched()
    {
        // GBK 编码的「项目约定」：不是合法 UTF-8，又没有 BOM 可认，判不出编码就不能动它
        var path = Path_("AGENTS.md");
        byte[] gbk = [0xCF, 0xEE, 0xC4, 0xBF, 0xD4, 0xBC, 0xB6, 0xA8, 0x0D, 0x0A];
        await File.WriteAllBytesAsync(path, gbk);

        var ex = await Should.ThrowAsync<IOException>(() => BriefMarkerWriter.WriteAsync(LocalHostFileSystem.Instance, path, "managed brief", CancellationToken.None));

        ex.Message.ShouldContain("AGENTS.md");
        (await File.ReadAllBytesAsync(path)).ShouldBe(gbk);
    }

    [Fact]
    public async Task Cleanup_LegacyEncodedFile_IsLeftUntouched()
    {
        var path = Path_("AGENTS.md");
        byte[] gbk = [0xCF, 0xEE, 0xC4, 0xBF, 0xD4, 0xBC];
        await File.WriteAllBytesAsync(path, gbk);

        await BriefMarkerWriter.CleanupAsync(LocalHostFileSystem.Instance, path, createdByUs: false, CancellationToken.None);

        (await File.ReadAllBytesAsync(path)).ShouldBe(gbk);
    }
}
