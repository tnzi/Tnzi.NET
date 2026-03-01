namespace Tnzi.AI.Coder.FileSystem;

/// <summary>
/// 文件系统工具组 — 读写、编辑、搜索文件
/// </summary>
[AIToolGroup("filesystem", "File System", "Read, write, edit, and search files")]
public class FileSystemTools : IAIToolProvider
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
    /// 复制文件
    /// </summary>
    [AIFunction("copy_file", "Copy a file from source to destination")]
    public async Task<object> CopyFileAsync(
        [AIParameter("source", "Source file path")] string source,
        [AIParameter("destination", "Destination file path")] string destination,
        [AIParameter("overwrite", "Overwrite if destination exists", false)] bool overwrite = false)
    {
        try
        {
            var srcValidation = await _pathValidator.ValidateAsync(source);
            if (!srcValidation.IsValid)
            {
                return new { error = $"Source: {srcValidation.Error}" };
            }

            var destValidation = await _pathValidator.ValidateAsync(destination);
            if (!destValidation.IsValid)
            {
                return new { error = $"Destination: {destValidation.Error}" };
            }

            var resolvedSrc = srcValidation.ResolvedPath!;
            var resolvedDest = destValidation.ResolvedPath!;

            if (!File.Exists(resolvedSrc))
            {
                return new { error = $"Source file not found: {source}" };
            }

            if (!overwrite && File.Exists(resolvedDest))
            {
                return new { error = $"Destination file already exists: {destination}. Use overwrite=true to replace." };
            }

            // 确保目标目录存在
            var destDir = Path.GetDirectoryName(resolvedDest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(resolvedSrc, resolvedDest, overwrite);

            _logger.LogDebug("Copied '{Source}' to '{Destination}'", source, destination);

            return new
            {
                success = true,
                source = resolvedSrc,
                destination = resolvedDest
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to copy file '{Source}' to '{Destination}'", source, destination);
            return new { error = $"Failed to copy file: {ex.Message}" };
        }
    }

    /// <summary>
    /// 移动/重命名文件
    /// </summary>
    [AIFunction("move_file", "Move or rename a file")]
    public async Task<object> MoveFileAsync(
        [AIParameter("source", "Source file path")] string source,
        [AIParameter("destination", "Destination file path")] string destination,
        [AIParameter("overwrite", "Overwrite if destination exists", false)] bool overwrite = false)
    {
        try
        {
            var srcValidation = await _pathValidator.ValidateAsync(source);
            if (!srcValidation.IsValid)
            {
                return new { error = $"Source: {srcValidation.Error}" };
            }

            var destValidation = await _pathValidator.ValidateAsync(destination);
            if (!destValidation.IsValid)
            {
                return new { error = $"Destination: {destValidation.Error}" };
            }

            var resolvedSrc = srcValidation.ResolvedPath!;
            var resolvedDest = destValidation.ResolvedPath!;

            if (!File.Exists(resolvedSrc))
            {
                return new { error = $"Source file not found: {source}" };
            }

            if (!overwrite && File.Exists(resolvedDest))
            {
                return new { error = $"Destination file already exists: {destination}. Use overwrite=true to replace." };
            }

            // 确保目标目录存在
            var destDir = Path.GetDirectoryName(resolvedDest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Move(resolvedSrc, resolvedDest, overwrite);

            _logger.LogDebug("Moved '{Source}' to '{Destination}'", source, destination);

            return new
            {
                success = true,
                source = resolvedSrc,
                destination = resolvedDest
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to move file '{Source}' to '{Destination}'", source, destination);
            return new { error = $"Failed to move file: {ex.Message}" };
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
    /// 查找匹配 glob 模式的文件
    /// </summary>
    [AIFunction("glob", "Find files matching a glob pattern")]
    public async Task<object> GlobAsync(
        [AIParameter("pattern", "Glob pattern (e.g. **/*.cs)")] string pattern,
        [AIParameter("path", "Search directory", false)] string? path = null,
        [AIParameter("respect_gitignore", "Exclude files matching .gitignore patterns", false)] bool respectGitignore = true)
    {
        try
        {
            var searchDir = path ?? _options.ProjectRoot;
            var validation = await _pathValidator.ValidateAsync(searchDir);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedDir = validation.ResolvedPath!;

            if (!Directory.Exists(resolvedDir))
            {
                return new { error = $"Directory not found: {searchDir}" };
            }

            // 加载 .gitignore 规则
            var gitignoreRules = respectGitignore
                ? LoadGitignoreRules(resolvedDir)
                : [];

            // 简单 glob 实现：将 glob 转换为 searchPattern
            var files = EnumerateFilesWithGlob(resolvedDir, pattern)
                .Where(f => !IsGitignored(f, resolvedDir, gitignoreRules))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Take(1000) // 限制返回数量
                .Select(f => Path.GetRelativePath(resolvedDir, f).Replace('\\', '/'))
                .ToList();

            _logger.LogDebug("Glob '{Pattern}' in '{Path}': found {Count} files (gitignore={Gitignore})",
                pattern, searchDir, files.Count, respectGitignore);

            return new
            {
                files,
                count = files.Count,
                truncated = files.Count >= 1000
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Glob failed for pattern '{Pattern}'", pattern);
            return new { error = $"Glob failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// 列出目录内容
    /// </summary>
    [AIFunction("list_directory", "List files and directories")]
    public async Task<object> ListDirectoryAsync(
        [AIParameter("path", "Directory path")] string path)
    {
        try
        {
            var validation = await _pathValidator.ValidateAsync(path);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedPath = validation.ResolvedPath!;

            if (!Directory.Exists(resolvedPath))
            {
                return new { error = $"Directory not found: {path}" };
            }

            var entries = new List<object>();

            foreach (var dir in Directory.GetDirectories(resolvedPath).OrderBy(d => d))
            {
                entries.Add(new
                {
                    name = Path.GetFileName(dir),
                    type = "directory"
                });
            }

            foreach (var file in Directory.GetFiles(resolvedPath).OrderBy(f => f))
            {
                var info = new FileInfo(file);
                entries.Add(new
                {
                    name = Path.GetFileName(file),
                    type = "file",
                    size = info.Length,
                    modified = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            _logger.LogDebug("Listed directory '{Path}': {Count} entries", path, entries.Count);

            return new
            {
                path = resolvedPath,
                entries,
                count = entries.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list directory '{Path}'", path);
            return new { error = $"Failed to list directory: {ex.Message}" };
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

    /// <summary>
    /// 使用简单 glob 模式枚举文件
    /// </summary>
    private static IEnumerable<string> EnumerateFilesWithGlob(string rootDir, string pattern)
    {
        // 处理 ** 递归模式
        var searchOption = pattern.Contains("**")
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        // 提取文件搜索模式（去除目录部分）
        var filePattern = pattern;
        if (pattern.Contains('/'))
        {
            filePattern = pattern.Split('/').Last();
        }
        else if (pattern.Contains("**/"))
        {
            filePattern = pattern.Replace("**/", "");
        }
        else if (pattern.StartsWith("**"))
        {
            filePattern = pattern[2..].TrimStart('/');
        }

        // 如果模式为空，匹配所有文件
        if (string.IsNullOrEmpty(filePattern))
        {
            filePattern = "*";
        }

        try
        {
            return Directory.EnumerateFiles(rootDir, filePattern, new EnumerationOptions
            {
                RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            });
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// 从搜索目录向上加载所有 .gitignore 规则（直到 ProjectRoot）
    /// </summary>
    private List<GitignoreRule> LoadGitignoreRules(string searchDir)
    {
        var rules = new List<GitignoreRule>();
        var projectRoot = Path.GetFullPath(_options.ProjectRoot).Replace('\\', '/');
        var currentDir = searchDir.Replace('\\', '/');

        // 从搜索目录往上遍历，直到 ProjectRoot
        while (true)
        {
            var gitignorePath = Path.Combine(currentDir.Replace('/', Path.DirectorySeparatorChar), ".gitignore");
            if (File.Exists(gitignorePath))
            {
                try
                {
                    var lines = File.ReadAllLines(gitignorePath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();

                        // 跳过空行和注释
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        {
                            continue;
                        }

                        var isNegation = trimmed.StartsWith('!');
                        var pattern = isNegation ? trimmed[1..] : trimmed;

                        rules.Add(new GitignoreRule(pattern, isNegation, currentDir));
                    }
                }
                catch
                {
                    // 忽略读取失败
                }
            }

            // 已到达 ProjectRoot 或更上层，停止
            if (currentDir.Equals(projectRoot, StringComparison.OrdinalIgnoreCase)
                || currentDir.Length <= projectRoot.Length)
            {
                break;
            }

            var parent = Path.GetDirectoryName(currentDir.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            currentDir = parent.Replace('\\', '/');
        }

        return rules;
    }

    /// <summary>
    /// 检查文件是否匹配 .gitignore 规则
    /// </summary>
    private static bool IsGitignored(string filePath, string rootDir, List<GitignoreRule> rules)
    {
        if (rules.Count == 0) return false;

        var normalizedPath = filePath.Replace('\\', '/');
        var relativePath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
        var fileName = Path.GetFileName(filePath);

        var ignored = false;

        foreach (var rule in rules)
        {
            var matches = MatchesGitignorePattern(relativePath, fileName, normalizedPath, rule);
            if (matches)
            {
                ignored = !rule.IsNegation;
            }
        }

        return ignored;
    }

    /// <summary>
    /// 检查路径是否匹配单个 .gitignore 模式
    /// </summary>
    private static bool MatchesGitignorePattern(string relativePath, string fileName, string fullPath, GitignoreRule rule)
    {
        var pattern = rule.Pattern.TrimEnd('/');
        var isDirectoryPattern = rule.Pattern.EndsWith('/');

        // 如果是目录模式，检查路径中是否包含该目录
        if (isDirectoryPattern)
        {
            return relativePath.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/" + pattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        // 扩展名匹配: *.log
        if (pattern.StartsWith('*') && !pattern.Contains('/'))
        {
            var extension = pattern[1..];
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        // 没有路径分隔符的模式：匹配文件名或路径中的目录名
        if (!pattern.Contains('/'))
        {
            // 精确匹配文件名
            if (fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // 匹配路径中的目录名
            return relativePath.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/" + pattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        // 路径模式：相对于 .gitignore 所在目录的匹配
        var ruleRelativePath = Path.GetRelativePath(
            rule.BaseDirectory.Replace('/', Path.DirectorySeparatorChar),
            fullPath.Replace('/', Path.DirectorySeparatorChar)).Replace('\\', '/');

        return SimpleWildcardMatch(ruleRelativePath, pattern);
    }

    /// <summary>
    /// 简单通配符匹配（支持 * 和 **）
    /// </summary>
    private static bool SimpleWildcardMatch(string path, string pattern)
    {
        // 将 pattern 转换为正则
        var regexPattern = "^"
            + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")     // ** 匹配任意路径
                .Replace("\\*", "[^/]*")     // * 匹配非路径分隔符
                .Replace("\\?", "[^/]")      // ? 匹配单个字符
            + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// .gitignore 规则
    /// </summary>
    private readonly record struct GitignoreRule(string Pattern, bool IsNegation, string BaseDirectory);
}
