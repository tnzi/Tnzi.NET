namespace Tnzi.AI.Coder.FileSystem;

/// <summary>
/// 文件系统工具组 — 读写、编辑、搜索文件
/// </summary>
[AIToolGroup("filesystem", "File System", "Read, write, edit, and search files")]
public partial class FileSystemTools : IAIToolProvider
{
    private readonly IPathValidator _pathValidator;
    private readonly CoderOptions _options;
    private readonly ILogger<FileSystemTools> _logger;

    public FileSystemTools(IPathValidator pathValidator, IOptions<CoderOptions> options, ILogger<FileSystemTools> logger)
    {
        _pathValidator = Check.NotNull(pathValidator);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 读取文件内容
    /// </summary>
    [AIFunction("read_file", "Read the contents of a file")]
    public async Task<object> ReadFileAsync(
        [AIParameter("path", "Absolute or relative file path")] string path,
        [AIParameter("offset", "Line offset (1-based)", false)] int? offset = null,
        [AIParameter("limit", "Max lines to read", false)] int? limit = null)
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

            // 检查文件大小
            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Length > _options.Sandbox.MaxFileReadSize)
            {
                return new { error = $"File size ({fileInfo.Length} bytes) exceeds maximum ({_options.Sandbox.MaxFileReadSize} bytes)" };
            }

            var lines = await File.ReadAllLinesAsync(resolvedPath);
            var totalLines = lines.Length;

            // 应用偏移和限制
            var startLine = offset.HasValue ? Math.Max(0, offset.Value - 1) : 0;
            var takeCount = limit ?? lines.Length;
            var selectedLines = lines.Skip(startLine).Take(takeCount).ToArray();

            // 构建带行号的输出
            var sb = new StringBuilder();
            for (var i = 0; i < selectedLines.Length; i++)
            {
                var lineNum = startLine + i + 1;
                sb.AppendLine($"{lineNum,6}\t{selectedLines[i]}");
            }

            _logger.LogDebug("Read file '{Path}': {Lines} lines (offset={Offset}, limit={Limit})",
                path, selectedLines.Length, offset, limit);

            return new
            {
                content = sb.ToString(),
                lines = selectedLines.Length,
                totalLines,
                path = resolvedPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read file '{Path}'", path);
            return new { error = $"Failed to read file: {ex.Message}" };
        }
    }

    /// <summary>
    /// 创建或覆盖文件
    /// </summary>
    [AIFunction("write_file", "Create or overwrite a file")]
    public async Task<object> WriteFileAsync(
        [AIParameter("path", "File path")] string path,
        [AIParameter("content", "Content to write")] string content)
    {
        try
        {
            var validation = await _pathValidator.ValidateAsync(path);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedPath = validation.ResolvedPath!;

            // 确保目录存在
            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(resolvedPath, content, Encoding.UTF8);
            var lineCount = content.Split('\n').Length;

            _logger.LogDebug("Wrote file '{Path}': {Lines} lines, {Length} chars", path, lineCount, content.Length);

            return new
            {
                success = true,
                path = resolvedPath,
                lines = lineCount,
                length = content.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write file '{Path}'", path);
            return new { error = $"Failed to write file: {ex.Message}" };
        }
    }

    /// <summary>
    /// 搜索并替换文件内容
    /// </summary>
    [AIFunction("edit_file", "Search and replace in a file")]
    public async Task<object> EditFileAsync(
        [AIParameter("path", "File path")] string path,
        [AIParameter("old_string", "Text to find")] string oldString,
        [AIParameter("new_string", "Replacement text")] string newString,
        [AIParameter("replace_all", "Replace all occurrences", false)] bool replaceAll = false)
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

            var content = await File.ReadAllTextAsync(resolvedPath);

            // 检查 old_string 是否存在
            var count = CountOccurrences(content, oldString);
            if (count == 0)
            {
                return new { error = "old_string not found in file" };
            }

            // 非 replaceAll 模式下，old_string 必须唯一
            if (!replaceAll && count > 1)
            {
                return new { error = $"old_string found {count} times. Use replace_all=true or provide more context to make it unique." };
            }

            // 执行替换
            string newContent;
            int replacements;

            if (replaceAll)
            {
                newContent = content.Replace(oldString, newString);
                replacements = count;
            }
            else
            {
                var index = content.IndexOf(oldString, StringComparison.Ordinal);
                newContent = string.Concat(content.AsSpan(0, index), newString, content.AsSpan(index + oldString.Length));
                replacements = 1;
            }

            await File.WriteAllTextAsync(resolvedPath, newContent, Encoding.UTF8);

            _logger.LogDebug("Edited file '{Path}': {Replacements} replacement(s)", path, replacements);

            return new
            {
                success = true,
                path = resolvedPath,
                replacements
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to edit file '{Path}'", path);
            return new { error = $"Failed to edit file: {ex.Message}" };
        }
    }

    /// <summary>
    /// 基于行号范围的文件编辑 — 比精确字符串匹配更健壮
    /// </summary>
    /// <remarks>
    /// 受 OpenHands llm_based_edit 启发。当 AI 无法精确匹配 old_string 时，
    /// 基于行号的编辑更可靠（尤其是大文件、缩进敏感代码）。
    /// </remarks>
    [AIFunction("edit_file_range", "Replace a range of lines in a file — more robust than exact string matching")]
    public async Task<object> EditFileRangeAsync(
        [AIParameter("path", "File path")] string path,
        [AIParameter("start_line", "Start line number (1-based, inclusive)")] int startLine,
        [AIParameter("end_line", "End line number (inclusive, -1 for end of file)")] int endLine,
        [AIParameter("content", "New content to replace the specified line range")] string content)
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

            var lines = (await File.ReadAllTextAsync(resolvedPath)).Split('\n');
            var totalLines = lines.Length;

            // 验证行号范围
            if (startLine < 1)
            {
                return new { error = "start_line must be >= 1" };
            }

            if (startLine > totalLines)
            {
                return new { error = $"start_line ({startLine}) exceeds total lines ({totalLines})" };
            }

            // -1 表示文件末尾
            var effectiveEndLine = endLine == -1 ? totalLines : endLine;

            if (effectiveEndLine < startLine)
            {
                return new { error = $"end_line ({endLine}) must be >= start_line ({startLine})" };
            }

            if (effectiveEndLine > totalLines)
            {
                effectiveEndLine = totalLines;
            }

            // 构建新文件内容
            var newLines = new List<string>();

            // 保留 start_line 之前的行
            for (var i = 0; i < startLine - 1; i++)
            {
                newLines.Add(lines[i]);
            }

            // 插入新内容
            var contentLines = content.Split('\n');
            newLines.AddRange(contentLines);

            // 保留 end_line 之后的行
            for (var i = effectiveEndLine; i < totalLines; i++)
            {
                newLines.Add(lines[i]);
            }

            var newContent = string.Join('\n', newLines);
            await File.WriteAllTextAsync(resolvedPath, newContent, Encoding.UTF8);

            var linesReplaced = effectiveEndLine - startLine + 1;
            var linesInserted = contentLines.Length;

            _logger.LogDebug("Edited file '{Path}' range [{Start}-{End}]: replaced {Replaced} lines with {Inserted} lines",
                path, startLine, effectiveEndLine, linesReplaced, linesInserted);

            return new
            {
                success = true,
                path = resolvedPath,
                lines_replaced = linesReplaced,
                lines_inserted = linesInserted,
                total_lines = newLines.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to edit file range '{Path}'", path);
            return new { error = $"Failed to edit file range: {ex.Message}" };
        }
    }

    /// <summary>
    /// 删除一个或多个文件
    /// </summary>
    [AIFunction("delete_files", "Delete one or more files")]
    public async Task<object> DeleteFilesAsync(
        [AIParameter("paths", "File paths to delete (comma-separated)")] string paths)
    {
        try
        {
            var filePaths = paths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (filePaths.Length == 0)
            {
                return new { error = "No file paths specified" };
            }

            var results = new List<object>();
            var successCount = 0;

            foreach (var filePath in filePaths)
            {
                var validation = await _pathValidator.ValidateAsync(filePath);
                if (!validation.IsValid)
                {
                    results.Add(new { path = filePath, success = false, error = validation.Error });
                    continue;
                }

                var resolvedPath = validation.ResolvedPath!;

                if (!File.Exists(resolvedPath))
                {
                    results.Add(new { path = filePath, success = false, error = "File not found" });
                    continue;
                }

                File.Delete(resolvedPath);
                results.Add(new { path = filePath, success = true, error = (string?)null });
                successCount++;
            }

            _logger.LogDebug("Deleted {Success}/{Total} files", successCount, filePaths.Length);

            return new
            {
                results,
                deleted = successCount,
                total = filePaths.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete files");
            return new { error = $"Failed to delete files: {ex.Message}" };
        }
    }

    /// <summary>
    /// 计算子串出现次数
    /// </summary>
    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
