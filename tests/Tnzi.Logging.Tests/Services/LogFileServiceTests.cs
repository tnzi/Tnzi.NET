using Tnzi.Logging.Dtos;

namespace Tnzi.Logging.Tests.Services;

/// <summary>
/// <see cref="LogFileService"/> 的行为契约。
///
/// 目录穿越拦截是文档里明写的卖点（"所有路径都经过规范化路径校验限制在
/// <c>BasePath</c> 之内"），此前**零测试** —— 卖点与实现之间没有任何东西
/// 在把它们钉在一起。
/// </summary>
public sealed class LogFileServiceTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingOptions _options;
    private readonly LogFileService _service;

    public LogFileServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tnzi-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new LoggingOptions { BasePath = _root };
        _service = new LogFileService(Microsoft.Extensions.Options.Options.Create(_options));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不该让测试失败
        }
    }

    private string WriteLog(string level, string fileName, params string[] lines)
    {
        var dir = Path.Combine(_root, level);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    // ---------- 路径校验 ----------

    [Theory]
    [InlineData("../../../Windows/System32/drivers/etc/hosts")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("log-/../../secrets.txt")]
    [InlineData("subdir/log-20260101.txt")]
    [InlineData("log-..20260101.txt")]
    public async Task Tail_RejectsFileNamesThatCouldEscapeTheLevelDirectory(string fileName)
    {
        WriteLog("Error", "log-20260101.txt", "irrelevant");

        var result = await _service.TailAsync("Error", fileName);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Theory]
    [InlineData("appsettings.json")]     // 不以 log- 开头
    [InlineData("log-20260101.log")]     // 不以 .txt 结尾
    [InlineData("")]
    [InlineData("   ")]
    public async Task Tail_RejectsFileNamesOutsideTheRollingConvention(string fileName)
    {
        var result = await _service.TailAsync("Error", fileName);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Theory]
    [InlineData("Nope")]
    [InlineData("../Error")]
    [InlineData("")]
    public async Task Tail_RejectsUnknownLevels(string level)
    {
        var result = await _service.TailAsync(level, "log-20260101.txt");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Tail_AcceptsTheRollingNamesSerilogActuallyWrites()
    {
        WriteLog("Error", "log-20260101.txt", "a", "b");
        WriteLog("Error", "log-.txt", "c");

        (await _service.TailAsync("Error", "log-20260101.txt")).Succeeded.ShouldBeTrue();
        (await _service.TailAsync("Error", "log-.txt")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Tail_ReturnsNotFoundForAValidNameThatDoesNotExist()
    {
        var result = await _service.TailAsync("Error", "log-20991231.txt");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Levels_AreCaseInsensitive()
    {
        WriteLog("Error", "log-20260101.txt", "x");

        (await _service.TailAsync("error", "log-20260101.txt")).Succeeded.ShouldBeTrue();
        (await _service.GetFilesAsync("ERROR")).Data!.Count.ShouldBe(1);
    }

    // ---------- 尾部读取 ----------

    [Fact]
    public async Task Tail_ReturnsTheLastNLinesInFileOrder()
    {
        WriteLog("Information", "log-20260101.txt", [.. Enumerable.Range(1, 100).Select(i => $"line {i}")]);

        var result = await _service.TailAsync("Information", "log-20260101.txt", lines: 5);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Lines.ShouldBe(["line 96", "line 97", "line 98", "line 99", "line 100"]);
    }

    /// <summary>
    /// 请求的行数多于文件所有行时，返回整个文件而不是空 —— 反向分块读到文件头
    /// 是这条实现里最容易写错的分支。
    /// </summary>
    [Fact]
    public async Task Tail_ReturnsTheWholeFileWhenItIsShorterThanTheWindow()
    {
        WriteLog("Information", "log-20260101.txt", "only", "three", "lines");

        var result = await _service.TailAsync("Information", "log-20260101.txt", lines: 500);

        result.Data!.Lines.ShouldBe(["only", "three", "lines"]);
    }

    /// <summary>
    /// 跨越 8 KB 分块边界：反向扫描要多读一块才能凑齐行数。
    /// </summary>
    [Fact]
    public async Task Tail_CrossesTheBackwardChunkBoundary()
    {
        var lines = Enumerable.Range(1, 400).Select(i => $"{i:D4} " + new string('x', 60)).ToArray();
        WriteLog("Information", "log-20260101.txt", lines);

        var result = await _service.TailAsync("Information", "log-20260101.txt", lines: 300);

        result.Data!.Lines.Count.ShouldBe(300);
        result.Data.Lines[0].ShouldStartWith("0101 ");
        result.Data.Lines[^1].ShouldStartWith("0400 ");
    }

    [Fact]
    public async Task Tail_HandlesAnEmptyFile()
    {
        WriteLog("Information", "log-20260101.txt");

        var result = await _service.TailAsync("Information", "log-20260101.txt");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tail_ClampsTheLineCount()
    {
        WriteLog("Information", "log-20260101.txt", "a");

        (await _service.TailAsync("Information", "log-20260101.txt", lines: -5)).Succeeded.ShouldBeTrue();
        (await _service.TailAsync("Information", "log-20260101.txt", lines: 1_000_000)).Succeeded.ShouldBeTrue();
    }

    // ---------- 检索 ----------

    [Fact]
    public async Task Search_FindsMatchesCaseInsensitivelyWithLineNumbers()
    {
        WriteLog("Error", "log-20260101.txt", "nothing", "a NullReferenceException here", "nothing");

        var result = await _service.SearchAsync("nullreferenceexception");

        result.Data!.Hits.Count.ShouldBe(1);
        result.Data.Hits[0].LineNumber.ShouldBe(2);
        result.Data.Hits[0].Level.ShouldBe("Error");
        result.Data.TruncationReason.ShouldBe(LogSearchTruncation.None);
        result.Data.Truncated.ShouldBeFalse();
    }

    /// <summary>
    /// ★ 搜索必须能读 Serilog 正在写的那个文件：<c>shared: true</c> 的文件 sink 常驻持有当天日志的写句柄，
    /// 而按修改时间倒序扫描时第一个打开的恰恰就是它。<c>FileInfo.OpenRead</c> 只给 <c>FileShare.Read</c>，
    /// 在 Windows 上对一个已有写句柄的文件必然共享冲突，整次搜索 500，偏偏是最需要搜的当天日志。
    /// </summary>
    [Fact]
    public async Task Search_ReadsAFileThatIsHeldOpenForWriting()
    {
        var path = WriteLog("Information", "log-20260101.txt", "starting", "a NullReferenceException here");
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        var result = await _service.SearchAsync("nullreferenceexception", level: "Information");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Hits.ShouldHaveSingleItem().LineNumber.ShouldBe(2);
    }

    /// <summary>Tail 走的是同一种共享读（对照组：它此前就是对的）。</summary>
    [Fact]
    public async Task Tail_ReadsAFileThatIsHeldOpenForWriting()
    {
        var path = WriteLog("Information", "log-20260101.txt", "one", "two");
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        var result = await _service.TailAsync("Information", "log-20260101.txt", 10);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Lines.ShouldBe(["one", "two"]);
    }

    /// <summary>
    /// 分级目录是「≥ 该级别」：一条 Error 同时写进 Information、Warning、Error 三个目录。
    /// 不指定级别的检索此前逐目录扫描、每处命中都计入，同一行返回三次，结果上限被重复挤占。
    /// </summary>
    [Fact]
    public async Task Search_WithoutALevel_ReturnsALineWrittenToSeveralLevelDirectoriesOnce()
    {
        const string errorLine = "2026-01-01 10:00:00.123 [ERR] a NullReferenceException here";
        WriteLog("Information", "log-20260101.txt", "2026-01-01 09:59:59.000 [INF] starting", errorLine);
        WriteLog("Warning", "log-20260101.txt", errorLine);
        WriteLog("Error", "log-20260101.txt", errorLine);

        var result = await _service.SearchAsync("nullreferenceexception");

        result.Data!.Hits.ShouldHaveSingleItem().Line.ShouldBe(errorLine);
    }

    [Fact]
    public async Task Search_WithoutALevel_KeepsALineThatOnlyAHigherLevelStillRetains()
    {
        // Error 目录保留得比 Information 久：只在 Error 目录里还在的那一天不能被当成重复丢掉
        WriteLog("Information", "log-20260301.txt", "2026-03-01 [ERR] boom recent");
        WriteLog("Error", "log-20260301.txt", "2026-03-01 [ERR] boom recent");
        WriteLog("Error", "log-20260101.txt", "2026-01-01 [ERR] boom old");

        var result = await _service.SearchAsync("boom");

        result.Data!.Hits.Select(h => h.Line).OrderBy(l => l, StringComparer.Ordinal)
            .ShouldBe(["2026-01-01 [ERR] boom old", "2026-03-01 [ERR] boom recent"]);
    }

    [Fact]
    public async Task Search_WithALevel_StillReturnsThatDirectorysCopy()
    {
        const string errorLine = "2026-01-01 10:00:00.123 [ERR] a NullReferenceException here";
        WriteLog("Information", "log-20260101.txt", errorLine);
        WriteLog("Error", "log-20260101.txt", errorLine);

        var result = await _service.SearchAsync("nullreferenceexception", level: "Error");

        result.Data!.Hits.ShouldHaveSingleItem().Level.ShouldBe("Error");
    }

    [Fact]
    public async Task Search_RequiresAKeyword()
    {
        var result = await _service.SearchAsync("   ");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Search_ReportsResultLimitTruncationDistinctly()
    {
        WriteLog("Error", "log-20260101.txt", [.. Enumerable.Repeat("boom", 50)]);

        var result = await _service.SearchAsync("boom", maxResults: 3);

        result.Data!.Hits.Count.ShouldBe(3);
        result.Data.Truncated.ShouldBeTrue();
        result.Data.TruncationReason.ShouldBe(LogSearchTruncation.ResultLimit);
    }

    /// <summary>
    /// ★ B2：**不命中**的检索必须被字节预算截住，而且要如实说是被预算截住的。
    ///
    /// 修复前这条路径没有任何上界：一个敲错的关键词会把整个保留窗口
    /// （Error 60 天、Fatal 90 天）顺序读完，且可以重复触发。
    /// </summary>
    [Fact]
    public async Task Search_StopsOnTheByteBudget_AndSaysSo()
    {
        _options.Search.MaxScanBytes = 200;
        WriteLog("Error", "log-20260101.txt", [.. Enumerable.Repeat(new string('y', 100), 200)]);

        var result = await _service.SearchAsync("no-such-keyword");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Hits.ShouldBeEmpty();
        result.Data.Truncated.ShouldBeTrue();
        result.Data.TruncationReason.ShouldBe(LogSearchTruncation.ByteBudget);
        result.Data.ScannedBytes.ShouldBeGreaterThan(200);
        // 预算是"读够就停"，不是"读完再报告"
        result.Data.ScannedBytes.ShouldBeLessThan(200 * 101L);
    }

    /// <summary>
    /// "预算耗尽"与"什么都没找到"对调用方必须是可区分的两件事。
    /// </summary>
    [Fact]
    public async Task Search_DistinguishesAnExhaustedBudgetFromAnHonestMiss()
    {
        WriteLog("Error", "log-20260101.txt", "alpha", "beta");

        var honestMiss = await _service.SearchAsync("gamma");
        honestMiss.Data!.Hits.ShouldBeEmpty();
        honestMiss.Data.Truncated.ShouldBeFalse();
        honestMiss.Data.TruncationReason.ShouldBe(LogSearchTruncation.None);

        _options.Search.MaxScanBytes = 1;
        var budgeted = await _service.SearchAsync("gamma");
        budgeted.Data!.Hits.ShouldBeEmpty();
        budgeted.Data.TruncationReason.ShouldBe(LogSearchTruncation.ByteBudget);
    }

    [Fact]
    public async Task Search_TreatsANonPositiveBudgetAsUnlimited()
    {
        _options.Search.MaxScanBytes = 0;
        _options.Search.MaxScanSeconds = 0;
        WriteLog("Error", "log-20260101.txt", [.. Enumerable.Repeat(new string('y', 100), 200)]);

        var result = await _service.SearchAsync("no-such-keyword");

        result.Data!.TruncationReason.ShouldBe(LogSearchTruncation.None);
        result.Data.ScannedBytes.ShouldBeGreaterThan(200 * 100L);
    }

    [Fact]
    public async Task Search_ReportsCancellationAsItsOwnReason()
    {
        WriteLog("Error", "log-20260101.txt", "alpha");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _service.SearchAsync("alpha", cancellationToken: cts.Token);

        result.Data!.TruncationReason.ShouldBe(LogSearchTruncation.Cancelled);
    }

    [Fact]
    public async Task Search_RejectsAnUnknownLevelFilter()
    {
        var result = await _service.SearchAsync("x", level: "Nope");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ---------- 列表 ----------

    [Fact]
    public async Task GetLevels_ListsEveryConfiguredLevelEvenWhenItsDirectoryIsMissing()
    {
        var levels = (await _service.GetLevelsAsync()).Data.ShouldNotBeNull();

        levels.Select(l => l.Level).ShouldBe(["Information", "Warning", "Error", "Fatal", "Debug"]);
        levels.Single(l => l.Level == "Debug").IsEnabled.ShouldBeFalse();
        levels.Single(l => l.Level == "Error").IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task GetFiles_ParsesTheRollingDateAndReturnsNewestFirst()
    {
        var older = WriteLog("Error", "log-20260101.txt", "a");
        var newer = WriteLog("Error", "log-20260102.txt", "b");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var files = (await _service.GetFilesAsync("Error")).Data.ShouldNotBeNull();

        files.Select(f => f.FileName).ShouldBe(["log-20260102.txt", "log-20260101.txt"]);
        files[0].RollingDate.ShouldBe(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetFiles_ReturnsEmptyForALevelWithNoDirectory()
    {
        var result = await _service.GetFilesAsync("Fatal");

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBeEmpty();
    }
}
