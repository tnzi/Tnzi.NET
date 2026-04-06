namespace Tnzi.AI.Coder.Diff;

/// <summary>
/// Diff 工具组 — 生成统一 diff 和应用补丁
/// </summary>
[AIToolGroup("diff", "Diff Operations", "Generate unified diffs and apply patches")]
public class DiffTools : IAIToolProvider
{
    private readonly IPathValidator _pathValidator;
    private readonly CoderOptions _options;
    private readonly ILogger<DiffTools> _logger;

    public DiffTools(IPathValidator pathValidator, IOptions<CoderOptions> options, ILogger<DiffTools> logger)
    {
        _pathValidator = Check.NotNull(pathValidator);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 在内存中生成两段文本的统一 diff
    /// </summary>
    [AIFunction("generate_diff", "Generate unified diff between two strings", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> GenerateDiffAsync(
        [AIParameter("old_content", "Original content")] string oldContent,
        [AIParameter("new_content", "Modified content")] string newContent,
        [AIParameter("filename", "Filename for diff header", false)] string? filename = null)
    {
        try
        {
            // 大文件保护
            if (oldContent.Length > 1_000_000 || newContent.Length > 1_000_000)
            {
                return Task.FromResult<object>(new { error = "Content too large for in-memory diff (max 1MB). Use git_diff instead." });
            }

            var oldLines = SplitLines(oldContent);
            var newLines = SplitLines(newContent);

            // 行数限制保护
            if (oldLines.Length > 5000 || newLines.Length > 5000)
            {
                return Task.FromResult<object>(new { error = "Content has too many lines for in-memory diff (max 5000 lines). Use git_diff instead." });
            }

            var diff = GenerateUnifiedDiff(oldLines, newLines, filename ?? "file");

            _logger.LogDebug("Generated diff: {OldLines} -> {NewLines} lines", oldLines.Length, newLines.Length);

            return Task.FromResult<object>(new
            {
                diff,
                has_changes = oldContent != newContent
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to generate diff");
            return Task.FromResult<object>(new { error = $"Failed to generate diff: {ex.Message}" });
        }
    }

    /// <summary>
    /// 将统一 diff 补丁应用到文件
    /// </summary>
    [AIFunction("apply_patch", "Apply a unified diff patch to a file", IsDestructive = true, SearchHint = "apply patch diff")]
    public async Task<object> ApplyPatchAsync(
        [AIParameter("path", "File path to patch")] string path,
        [AIParameter("diff", "Unified diff content to apply")] string diff)
    {
        try
        {
            var validation = await _pathValidator.ValidateAsync(path);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedPath = validation.ResolvedPath!;

            if (!File.Exists(resolvedPath))
            {
                return new { error = $"File not found: {path}" };
            }

            var originalContent = await File.ReadAllTextAsync(resolvedPath);
            var originalLines = SplitLines(originalContent).ToList();

            var hunks = ParseUnifiedDiff(diff);
            if (hunks.Count == 0)
            {
                return new { error = "No valid hunks found in diff" };
            }

            // 从后往前应用 hunks（避免行号偏移）
            var resultLines = new List<string>(originalLines);
            foreach (var hunk in hunks.OrderByDescending(h => h.OldStart))
            {
                // 转为 0-based
                var startIndex = hunk.OldStart - 1;
                if (startIndex < 0 || startIndex > resultLines.Count)
                {
                    return new { error = $"Hunk at line {hunk.OldStart} is out of range (file has {resultLines.Count} lines)" };
                }

                // 移除旧行
                var removeCount = Math.Min(hunk.OldCount, resultLines.Count - startIndex);
                resultLines.RemoveRange(startIndex, removeCount);

                // 插入新行
                resultLines.InsertRange(startIndex, hunk.NewLines);
            }

            var newContent = string.Join("\n", resultLines);
            // 保留原始文件的结尾换行符
            if (originalContent.EndsWith('\n'))
            {
                newContent += "\n";
            }

            await File.WriteAllTextAsync(resolvedPath, newContent, Encoding.UTF8);

            _logger.LogDebug("Applied {HunkCount} hunk(s) to '{Path}'", hunks.Count, path);

            return new
            {
                success = true,
                path = resolvedPath,
                hunks_applied = hunks.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to apply patch to '{Path}'", path);
            return new { error = $"Failed to apply patch: {ex.Message}" };
        }
    }

    /// <summary>
    /// 将文本按行拆分（兼容 \r\n 和 \n）
    /// </summary>
    private static string[] SplitLines(string text)
    {
        return text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
    }

    /// <summary>
    /// 使用 LCS 算法生成统一 diff 格式
    /// </summary>
    private static string GenerateUnifiedDiff(string[] oldLines, string[] newLines, string filename)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{filename}");
        sb.AppendLine($"+++ b/{filename}");

        // 计算 LCS 编辑序列
        var edits = ComputeEdits(oldLines, newLines);

        // 将编辑序列分组为 hunks
        var hunks = GroupEditsIntoHunks(edits, oldLines, newLines, contextLines: 3);

        foreach (var hunk in hunks)
        {
            sb.AppendLine($"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@");
            foreach (var line in hunk.Lines)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 计算编辑操作序列（基于 LCS）
    /// </summary>
    private static List<EditOp> ComputeEdits(string[] oldLines, string[] newLines)
    {
        var m = oldLines.Length;
        var n = newLines.Length;

        // 构建完整 LCS 表用于回溯
        var dp = new int[m + 1][];
        for (var i = 0; i <= m; i++)
        {
            dp[i] = new int[n + 1];
        }

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                dp[i][j] = oldLines[i - 1] == newLines[j - 1]
                    ? dp[i - 1][j - 1] + 1
                    : Math.Max(dp[i - 1][j], dp[i][j - 1]);
            }
        }

        // 回溯生成编辑操作
        var edits = new List<EditOp>();
        var oi = m;
        var ni = n;

        while (oi > 0 || ni > 0)
        {
            if (oi > 0 && ni > 0 && oldLines[oi - 1] == newLines[ni - 1])
            {
                edits.Add(new EditOp(EditType.Equal, oi - 1, ni - 1));
                oi--;
                ni--;
            }
            else if (ni > 0 && (oi == 0 || dp[oi][ni - 1] >= dp[oi - 1][ni]))
            {
                edits.Add(new EditOp(EditType.Insert, -1, ni - 1));
                ni--;
            }
            else
            {
                edits.Add(new EditOp(EditType.Delete, oi - 1, -1));
                oi--;
            }
        }

        edits.Reverse();
        return edits;
    }

    /// <summary>
    /// 将编辑操作分组为带上下文的 hunks
    /// </summary>
    private static List<DiffHunk> GroupEditsIntoHunks(List<EditOp> edits, string[] oldLines, string[] newLines, int contextLines)
    {
        var hunks = new List<DiffHunk>();
        if (edits.Count == 0) return hunks;

        // 找到变更区域
        var changeIndices = new List<int>();
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Type != EditType.Equal)
            {
                changeIndices.Add(i);
            }
        }

        if (changeIndices.Count == 0) return hunks;

        // 按相邻性分组
        var groups = new List<(int start, int end)>();
        var groupStart = changeIndices[0];
        var groupEnd = changeIndices[0];

        for (var i = 1; i < changeIndices.Count; i++)
        {
            // 如果两个变更之间的 equal 行 <= 2*contextLines，合并
            var equalsBetween = changeIndices[i] - groupEnd - 1;
            if (equalsBetween <= contextLines * 2)
            {
                groupEnd = changeIndices[i];
            }
            else
            {
                groups.Add((groupStart, groupEnd));
                groupStart = changeIndices[i];
                groupEnd = changeIndices[i];
            }
        }
        groups.Add((groupStart, groupEnd));

        // 为每个组生成 hunk
        foreach (var (start, end) in groups)
        {
            var hunkStart = Math.Max(0, start - contextLines);
            var hunkEnd = Math.Min(edits.Count - 1, end + contextLines);

            var lines = new List<string>();
            var oldStart = -1;
            var newStart = -1;
            var oldCount = 0;
            var newCount = 0;

            for (var i = hunkStart; i <= hunkEnd; i++)
            {
                var edit = edits[i];
                switch (edit.Type)
                {
                    case EditType.Equal:
                        if (oldStart == -1) { oldStart = edit.OldIndex; newStart = edit.NewIndex; }
                        lines.Add($" {oldLines[edit.OldIndex]}");
                        oldCount++;
                        newCount++;
                        break;
                    case EditType.Delete:
                        if (oldStart == -1) { oldStart = edit.OldIndex; newStart = i < edits.Count - 1 ? FindNextNewIndex(edits, i) : 0; }
                        lines.Add($"-{oldLines[edit.OldIndex]}");
                        oldCount++;
                        break;
                    case EditType.Insert:
                        if (oldStart == -1) { oldStart = i < edits.Count - 1 ? FindNextOldIndex(edits, i) : 0; newStart = edit.NewIndex; }
                        lines.Add($"+{newLines[edit.NewIndex]}");
                        newCount++;
                        break;
                }
            }

            // 转为 1-based
            hunks.Add(new DiffHunk
            {
                OldStart = (oldStart < 0 ? 0 : oldStart) + 1,
                OldCount = oldCount,
                NewStart = (newStart < 0 ? 0 : newStart) + 1,
                NewCount = newCount,
                Lines = lines
            });
        }

        return hunks;
    }

    private static int FindNextOldIndex(List<EditOp> edits, int fromIndex)
    {
        for (var i = fromIndex + 1; i < edits.Count; i++)
        {
            if (edits[i].OldIndex >= 0) return edits[i].OldIndex;
        }
        return 0;
    }

    private static int FindNextNewIndex(List<EditOp> edits, int fromIndex)
    {
        for (var i = fromIndex + 1; i < edits.Count; i++)
        {
            if (edits[i].NewIndex >= 0) return edits[i].NewIndex;
        }
        return 0;
    }

    /// <summary>
    /// 解析统一 diff 格式为 hunk 列表
    /// </summary>
    private static List<PatchHunk> ParseUnifiedDiff(string diff)
    {
        var hunks = new List<PatchHunk>();
        var lines = diff.Split('\n');

        PatchHunk? currentHunk = null;

        foreach (var line in lines)
        {
            // 跳过文件头
            if (line.StartsWith("---") || line.StartsWith("+++"))
            {
                continue;
            }

            // 解析 hunk 头 @@ -oldStart,oldCount +newStart,newCount @@
            if (line.StartsWith("@@"))
            {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
                if (match.Success)
                {
                    currentHunk = new PatchHunk
                    {
                        OldStart = int.Parse(match.Groups[1].Value),
                        OldCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
                        NewLines = []
                    };
                    hunks.Add(currentHunk);
                }
                continue;
            }

            if (currentHunk == null) continue;

            if (line.StartsWith('+'))
            {
                // 新增行
                currentHunk.NewLines.Add(line[1..]);
            }
            else if (line.StartsWith('-'))
            {
                // 删除行（由 OldStart + OldCount 处理）
            }
            else if (line.StartsWith(' '))
            {
                // 上下文行（保留）
                currentHunk.NewLines.Add(line[1..]);
            }
        }

        return hunks;
    }

    /// <summary>
    /// 编辑操作类型
    /// </summary>
    private enum EditType { Equal, Insert, Delete }

    /// <summary>
    /// 单个编辑操作
    /// </summary>
    private readonly record struct EditOp(EditType Type, int OldIndex, int NewIndex);

    /// <summary>
    /// diff 输出中的 hunk
    /// </summary>
    private class DiffHunk
    {
        public int OldStart { get; set; }
        public int OldCount { get; set; }
        public int NewStart { get; set; }
        public int NewCount { get; set; }
        public List<string> Lines { get; set; } = [];
    }

    /// <summary>
    /// 补丁应用中的 hunk
    /// </summary>
    private class PatchHunk
    {
        public int OldStart { get; set; }
        public int OldCount { get; set; }
        public List<string> NewLines { get; set; } = [];
    }
}
