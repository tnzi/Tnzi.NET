using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Tnzi.Logging.Services;

/// <summary>
/// Filesystem-bound read-only log access. Constrains every IO call to
/// the configured <see cref="LoggingOptions.BasePath"/> via canonicalised
/// path validation.
/// </summary>
public class LogFileService : ILogFileService
{
    // Hard caps to bound response size on the wire and CPU/memory server-side.
    // Set generously enough for the operational use case (debug a recent
    // incident) but small enough that malicious calls can't pin the process.
    private const int TailLineCapMax = 5000;
    private const int TailLineCapDefault = 500;
    private const int SearchResultsCapMax = 1000;
    private const int SearchResultsCapDefault = 200;

    // 每读这么多行检查一次时间预算（掩码要求是 2^n - 1）。
    private const int TimeCheckLineMask = 1023;

    // Recognised level directory names; anything else passed to a level
    // parameter is rejected.
    private static readonly string[] ValidLevels =
        ["Information", "Warning", "Error", "Fatal", "Debug"];

    // Filename pattern Serilog writes: `log-YYYYMMDD.txt` (day-rolling).
    private static readonly Regex RollingDateRegex =
        new(@"log-(\d{4})(\d{2})(\d{2})\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly LoggingOptions _options;

    public LogFileService(IOptions<LoggingOptions> options)
    {
        _options = Check.NotNull(options).Value;
    }

    public Task<Result<List<LogLevelInfoDto>>> GetLevelsAsync(CancellationToken cancellationToken = default)
    {
        var basePath = ResolveBasePath();
        var levels = new List<LogLevelInfoDto>();
        foreach (var levelName in ValidLevels)
        {
            var enabled = IsLevelEnabled(levelName);
            var levelDir = Path.Combine(basePath, levelName);
            var info = new LogLevelInfoDto
            {
                Level = levelName,
                IsEnabled = enabled,
            };
            if (Directory.Exists(levelDir))
            {
                var files = Directory.EnumerateFiles(levelDir, "log-*.txt", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f))
                    .ToList();
                info.FileCount = files.Count;
                info.TotalSize = files.Sum(f => f.Length);
                info.LastModifiedUtc = files.Count > 0
                    ? files.Max(f => f.LastWriteTimeUtc)
                    : null;
            }
            levels.Add(info);
        }
        return Task.FromResult(Result<List<LogLevelInfoDto>>.Success(levels));
    }

    public Task<Result<List<LogFileInfoDto>>> GetFilesAsync(string level, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeLevel(level);
        if (normalized == null)
        {
            return Task.FromResult(Result<List<LogFileInfoDto>>.Failure("Invalid log level", 400));
        }
        var basePath = ResolveBasePath();
        var levelDir = Path.Combine(basePath, normalized);
        var files = new List<LogFileInfoDto>();
        if (!Directory.Exists(levelDir))
        {
            return Task.FromResult(Result<List<LogFileInfoDto>>.Success(files));
        }
        foreach (var path in Directory.EnumerateFiles(levelDir, "log-*.txt", SearchOption.TopDirectoryOnly))
        {
            var fi = new FileInfo(path);
            files.Add(new LogFileInfoDto
            {
                Level = normalized,
                FileName = fi.Name,
                Size = fi.Length,
                LastModifiedUtc = fi.LastWriteTimeUtc,
                RollingDate = TryParseRollingDate(fi.Name),
            });
        }
        files.Sort((a, b) => b.LastModifiedUtc.CompareTo(a.LastModifiedUtc));
        return Task.FromResult(Result<List<LogFileInfoDto>>.Success(files));
    }

    public async Task<Result<LogTailResultDto>> TailAsync(string level, string fileName, int lines = TailLineCapDefault, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeLevel(level);
        if (normalized == null)
        {
            return Result<LogTailResultDto>.Failure("Invalid log level", 400);
        }
        var safeFile = ValidateFileName(fileName);
        if (safeFile == null)
        {
            return Result<LogTailResultDto>.Failure("Invalid file name", 400);
        }
        var basePath = ResolveBasePath();
        var fullPath = Path.GetFullPath(Path.Combine(basePath, normalized, safeFile));
        if (!IsUnderBase(fullPath, basePath))
        {
            return Result<LogTailResultDto>.Failure("Path is outside log directory", 400);
        }
        if (!File.Exists(fullPath))
        {
            return Result<LogTailResultDto>.Failure("Log file not found", 404);
        }
        var clamped = Math.Clamp(lines, 1, TailLineCapMax);
        var fi = new FileInfo(fullPath);
        var tailLines = await ReadLastLinesAsync(fullPath, clamped, cancellationToken).ConfigureAwait(false);
        return Result<LogTailResultDto>.Success(new LogTailResultDto
        {
            Level = normalized,
            FileName = safeFile,
            TotalSize = fi.Length,
            Lines = tailLines,
            Truncated = tailLines.Count >= clamped,
        });
    }

    public async Task<Result<LogSearchResultDto>> SearchAsync(
        string keyword,
        string? level = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int maxResults = SearchResultsCapDefault,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Result<LogSearchResultDto>.Failure("Keyword is required", 400);
        }
        var basePath = ResolveBasePath();
        var levelsToScan = new List<string>();
        if (!string.IsNullOrWhiteSpace(level))
        {
            var n = NormalizeLevel(level);
            if (n == null)
            {
                return Result<LogSearchResultDto>.Failure("Invalid log level", 400);
            }
            levelsToScan.Add(n);
        }
        else
        {
            levelsToScan.AddRange(ValidLevels);
        }

        var cap = Math.Clamp(maxResults, 1, SearchResultsCapMax);
        var sw = Stopwatch.StartNew();
        var result = new LogSearchResultDto
        {
            Levels = levelsToScan,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Keyword = keyword,
        };

        // 扫描预算。关键词命中时循环很快停在结果上限；代价全在**不命中**的那次 ——
        // 它会把整个保留窗口读完（Error 60 天、Fatal 90 天）。没有预算，一个敲错的
        // 关键词就是一次可重复的顺序全盘读。
        var budget = _options.Search ?? new LogSearchOptions();
        var maxBytes = budget.MaxScanBytes > 0 ? budget.MaxScanBytes : long.MaxValue;
        var maxTicks = budget.MaxScanSeconds > 0
            ? (long)(budget.MaxScanSeconds * Stopwatch.Frequency)
            : long.MaxValue;
        var scannedBytes = 0L;
        var stop = LogSearchTruncation.None;
        var duplicates = new CrossLevelDuplicateFilter();

        foreach (var lv in levelsToScan)
        {
            if (stop != LogSearchTruncation.None) break;
            duplicates.BeginLevel();
            var levelDir = Path.Combine(basePath, lv);
            if (!Directory.Exists(levelDir)) continue;
            var files = Directory.EnumerateFiles(levelDir, "log-*.txt", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    stop = LogSearchTruncation.Cancelled;
                    break;
                }
                if (fromUtc.HasValue && file.LastWriteTimeUtc < fromUtc.Value) continue;
                if (toUtc.HasValue && file.LastWriteTimeUtc > toUtc.Value.AddDays(1)) continue;
                var lineNumber = 0;
                FileStream stream;
                try
                {
                    stream = OpenForSharedRead(file.FullName);
                }
                catch (FileNotFoundException)
                {
                    // 枚举之后、打开之前被保留期清理删掉了：这个文件已经不存在，不是错误
                    continue;
                }
                using var reader = new StreamReader(stream, Encoding.UTF8);
                // 用 ReadLineAsync 返回 null 判定文件结束，而不是 reader.EndOfStream ——
                // 后者会在异步方法里做一次同步阻塞读（CA2024）。
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    lineNumber++;
                    // 行长度按字符计而不是重新编码一次 —— 这是预算不是计费，
                    // 日志行以 ASCII 为主，差一个常数因子不影响它挡住什么。
                    scannedBytes += line.Length + 1;
                    if (scannedBytes > maxBytes)
                    {
                        stop = LogSearchTruncation.ByteBudget;
                        break;
                    }
                    // 时间预算按行数抽查：ElapsedTicks 本身很便宜，但百万行级别上
                    // 每行一次仍是纯开销，而预算的粒度到不了这么细。
                    if ((lineNumber & TimeCheckLineMask) == 0 && sw.ElapsedTicks > maxTicks)
                    {
                        stop = LogSearchTruncation.TimeBudget;
                        break;
                    }
                    // 内存比较用 OrdinalIgnoreCase，避免为每一行额外分配一份小写副本
                    if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        && !duplicates.IsCopyFromAnEarlierLevel(file.Name, line))
                    {
                        result.Hits.Add(new LogSearchHitDto
                        {
                            Level = lv,
                            FileName = file.Name,
                            LineNumber = lineNumber,
                            Line = line,
                        });
                        if (result.Hits.Count >= cap)
                        {
                            stop = LogSearchTruncation.ResultLimit;
                            break;
                        }
                    }
                }
                if (stop != LogSearchTruncation.None) break;
            }
        }
        sw.Stop();
        result.ScannedBytes = scannedBytes;
        result.TruncationReason = stop;
        result.Truncated = stop != LogSearchTruncation.None;
        result.ElapsedMs = sw.ElapsedMilliseconds;
        return Result<LogSearchResultDto>.Success(result);
    }

    // ---------- Helpers ----------

    /// <summary>
    /// 跨级别目录的重复命中过滤。级别目录是「≥ 该级别」：一条 Error 同时写在 Information、Warning、Error 三个目录的
    /// 同名日期文件里，不指定级别的检索逐目录扫描时同一行会命中多次，挤占结果上限。
    /// </summary>
    /// <remarks>
    /// 按（文件名, 行文本）计数而不是按集合去重：同一目录里两条逐字相同的行（同一毫秒、同一消息）是两次真实发生，
    /// 都要留下；只有后扫的目录里不超过此前某个目录已出现次数的那几条才是副本。保留期较长的高级别目录里
    /// 低级别目录已经清掉的那些天，此前没有出现过，照常计为命中。
    /// </remarks>
    private sealed class CrossLevelDuplicateFilter
    {
        private readonly Dictionary<(string FileName, string Line), int> _seenInEarlierLevels = [];
        private Dictionary<(string FileName, string Line), int> _seenInCurrentLevel = [];

        /// <summary>开始扫描下一个级别目录：把上一个目录的计数并进「此前各目录的最大出现次数」。</summary>
        public void BeginLevel()
        {
            foreach (var (key, count) in _seenInCurrentLevel)
            {
                if (count > _seenInEarlierLevels.GetValueOrDefault(key))
                {
                    _seenInEarlierLevels[key] = count;
                }
            }

            _seenInCurrentLevel = [];
        }

        /// <summary>这一行是不是此前某个级别目录里已经命中过的那一条的副本。</summary>
        public bool IsCopyFromAnEarlierLevel(string fileName, string line)
        {
            var key = (fileName, line);
            var occurrence = _seenInCurrentLevel.GetValueOrDefault(key) + 1;
            _seenInCurrentLevel[key] = occurrence;
            return occurrence <= _seenInEarlierLevels.GetValueOrDefault(key);
        }
    }

    private string ResolveBasePath()
    {
        // BasePath may be relative (default "Logs"), so resolve it against the
        // current process directory the same way Serilog does at writer setup.
        var raw = _options.BasePath;
        return Path.GetFullPath(raw);
    }

    private static string? NormalizeLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level)) return null;
        var match = ValidLevels.FirstOrDefault(l =>
            string.Equals(l, level, StringComparison.OrdinalIgnoreCase));
        return match;
    }

    private bool IsLevelEnabled(string level)
    {
        return level switch
        {
            "Information" => _options.FileOutput.Information.Enabled,
            "Warning" => _options.FileOutput.Warning.Enabled,
            "Error" => _options.FileOutput.Error.Enabled,
            "Fatal" => _options.FileOutput.Fatal.Enabled,
            "Debug" => _options.FileOutput.Debug.Enabled,
            _ => false,
        };
    }

    /// <summary>
    /// Reject file names containing directory separators or `..` segments,
    /// these would let a caller escape the level directory. Allow only the
    /// rolling pattern `log-YYYYMMDD.txt` plus its no-date sibling `log-.txt`
    /// Serilog uses for the latest day before rollover.
    /// </summary>
    private static string? ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        if (fileName.Contains('/') || fileName.Contains('\\')) return null;
        if (fileName.Contains("..")) return null;
        if (!fileName.StartsWith("log-", StringComparison.Ordinal)) return null;
        if (!fileName.EndsWith(".txt", StringComparison.Ordinal)) return null;
        return fileName;
    }

    private static bool IsUnderBase(string fullPath, string basePath)
    {
        var normalizedBase = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? TryParseRollingDate(string fileName)
    {
        var m = RollingDateRegex.Match(fileName);
        if (!m.Success) return null;
        if (int.TryParse(m.Groups[1].Value, out var y)
            && int.TryParse(m.Groups[2].Value, out var mn)
            && int.TryParse(m.Groups[3].Value, out var d))
        {
            try
            {
                return new DateTime(y, mn, d, 0, 0, 0, DateTimeKind.Utc);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// 以只读方式打开日志文件，同时容许别的句柄在写它、删它。
    /// </summary>
    /// <remarks>
    /// ★ Serilog 的文件 sink（<c>shared: true</c>）常驻持有当天日志的写句柄、滚动与保留期清理会删文件。
    /// <c>FileInfo.OpenRead</c> 只给 <c>FileShare.Read</c>，在 Windows 上对一个已有写句柄的文件必然共享冲突：
    /// 搜索此前就是这样在最需要搜的当天日志上 500 的。读日志的每一条路径都必须经这里打开。
    /// </remarks>
    private static FileStream OpenForSharedRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Stream `count` lines from the end of the file without loading the
    /// whole file into memory. Strategy: seek to end, read chunks backwards,
    /// counting newlines, until N newlines (or file start) are found, then
    /// forward-scan the resulting window once to produce the line list.
    /// </summary>
    private static async Task<List<string>> ReadLastLinesAsync(string path, int count, CancellationToken cancellationToken)
    {
        const int chunkSize = 8192;
        using var stream = OpenForSharedRead(path);
        var length = stream.Length;
        if (length == 0) return new List<string>();

        var newlineCount = 0;
        var pos = length;
        var buffer = new byte[chunkSize];

        while (pos > 0 && newlineCount <= count)
        {
            var readSize = (int)Math.Min(chunkSize, pos);
            pos -= readSize;
            stream.Seek(pos, SeekOrigin.Begin);
            var read = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken).ConfigureAwait(false);
            for (var i = read - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    newlineCount++;
                    if (newlineCount > count)
                    {
                        // Found one more newline than needed, so the start position
                        // is just past this newline.
                        pos += i + 1;
                        goto SeekAndRead;
                    }
                }
            }
        }
        // Fewer than `count` newlines in the file: return the entire file.
        pos = 0;

    SeekAndRead:
        stream.Seek(pos, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }
        // The backwards scan can over-count by one newline at EOF; trim to N.
        if (lines.Count > count)
        {
            lines.RemoveRange(0, lines.Count - count);
        }
        return lines;
    }
}
