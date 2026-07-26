using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Tnzi.Logging.Dtos;
using Tnzi.Results;

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

        foreach (var lv in levelsToScan)
        {
            var levelDir = Path.Combine(basePath, lv);
            if (!Directory.Exists(levelDir)) continue;
            var files = Directory.EnumerateFiles(levelDir, "log-*.txt", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (fromUtc.HasValue && file.LastWriteTimeUtc < fromUtc.Value) continue;
                if (toUtc.HasValue && file.LastWriteTimeUtc > toUtc.Value.AddDays(1)) continue;
                var lineNumber = 0;
                using var reader = new StreamReader(file.OpenRead(), Encoding.UTF8);
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null) break;
                    lineNumber++;
                    // 内存比较用 OrdinalIgnoreCase，避免为每一行额外分配一份小写副本
                    if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
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
                            result.Truncated = true;
                            break;
                        }
                    }
                }
                if (result.Truncated) break;
            }
            if (result.Truncated) break;
        }
        sw.Stop();
        result.ElapsedMs = sw.ElapsedMilliseconds;
        return Result<LogSearchResultDto>.Success(result);
    }

    // ---------- Helpers ----------

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
    /// Stream `count` lines from the end of the file without loading the
    /// whole file into memory. Strategy: seek to end, read chunks backwards,
    /// counting newlines, until N newlines (or file start) are found, then
    /// forward-scan the resulting window once to produce the line list.
    /// </summary>
    private static async Task<List<string>> ReadLastLinesAsync(string path, int count, CancellationToken cancellationToken)
    {
        const int chunkSize = 8192;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null) break;
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
