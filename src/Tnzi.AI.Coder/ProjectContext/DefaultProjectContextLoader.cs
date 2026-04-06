namespace Tnzi.AI.Coder.ProjectContext;

/// <summary>
/// 默认项目上下文加载器 — 从文件系统加载项目指令文件
/// </summary>
/// <remarks>
/// 加载顺序（后面的优先级更高，内容拼接）：
/// 1. ~/.tnzi/TNZI.md （全局指令）
/// 2. ~/.claude/CLAUDE.md （Claude/Codex 兼容全局指令）
/// 3. {ProjectRoot}/TNZI.md
/// 4. {ProjectRoot}/AGENTS.md
/// 5. {ProjectRoot}/CLAUDE.md
/// 6. {ProjectRoot}/.tnzi/TNZI.md
/// 7. {ProjectRoot}/.claude/CLAUDE.md
/// 内容通过 \n---\n 分隔符合并
/// </remarks>
public class DefaultProjectContextLoader : IProjectContextLoader
{
    private readonly CoderOptions _options;
    private readonly ILogger<DefaultProjectContextLoader> _logger;

    public DefaultProjectContextLoader(IOptions<CoderOptions> options, ILogger<DefaultProjectContextLoader> logger)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ProjectContextData> LoadAsync(string? projectRoot = null, CancellationToken ct = default)
    {
        var resolvedRoot = Path.GetFullPath(projectRoot ?? _options.ProjectRoot);
        var result = new ProjectContextData
        {
            ProjectRoot = resolvedRoot
        };

        var contentParts = new List<string>();
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_options.IncludeGlobalInstructionFiles)
        {
            foreach (var globalInstructionPath in GetGlobalInstructionFiles())
            {
                await TryLoadFileAsync(globalInstructionPath, contentParts, result, loadedPaths, ct);
            }
        }

        // 项目指令文件（按配置的顺序加载）
        foreach (var relativePath in _options.InstructionFiles)
        {
            var fullPath = Path.Combine(resolvedRoot, relativePath);
            await TryLoadFileAsync(fullPath, contentParts, result, loadedPaths, ct);
        }

        // 合并所有内容
        if (contentParts.Count > 0)
        {
            result.Instructions = string.Join("\n---\n", contentParts);
        }

        _logger.LogDebug("Loaded project context from {Root}: {FileCount} files, {InstructionLength} chars",
            resolvedRoot, result.LoadedFiles.Count,
            result.Instructions?.Length ?? 0);

        return result;
    }

    /// <summary>
    /// 尝试加载单个指令文件
    /// </summary>
    private async Task TryLoadFileAsync(
        string filePath,
        List<string> contentParts,
        ProjectContextData result,
        HashSet<string> loadedPaths,
        CancellationToken ct)
    {
        var normalizedPath = NormalizePath(filePath);
        if (normalizedPath == null || !loadedPaths.Add(normalizedPath) || !File.Exists(normalizedPath))
        {
            return;
        }

        try
        {
            var rawContent = await File.ReadAllTextAsync(normalizedPath, Encoding.UTF8, ct);

            // 解析 YAML frontmatter
            var (content, metadata) = ParseFrontmatter(rawContent);

            // 合并元数据
            foreach (var kv in metadata)
            {
                result.Metadata[kv.Key] = kv.Value;
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                contentParts.Add(content.Trim());
            }

            result.LoadedFiles.Add(normalizedPath);
            _logger.LogDebug("Loaded instruction file: {Path} ({Length} chars)", normalizedPath, content.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load instruction file: {Path}", normalizedPath);
        }
    }

    private static IEnumerable<string> GetGlobalInstructionFiles()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDir))
        {
            yield break;
        }

        yield return Path.Combine(homeDir, ".tnzi", "TNZI.md");
        yield return Path.Combine(homeDir, ".claude", "CLAUDE.md");
    }

    private static string? NormalizePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(filePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析 YAML frontmatter
    /// </summary>
    /// <remarks>
    /// 简单解析：文件以 --- 开头，到下一个 --- 之间的内容为 frontmatter。
    /// frontmatter 中每行格式为 key: value
    /// </remarks>
    private static (string content, Dictionary<string, string> metadata) ParseFrontmatter(string rawContent)
    {
        var metadata = new Dictionary<string, string>();

        if (!rawContent.StartsWith("---"))
        {
            return (rawContent, metadata);
        }

        var lines = rawContent.Split('\n');
        var endIndex = -1;

        // 查找结束的 ---（从第二行开始）
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex < 0)
        {
            // 没有找到结束标记，整个内容都是普通文本
            return (rawContent, metadata);
        }

        // 解析 frontmatter 行
        for (var i = 1; i < endIndex; i++)
        {
            var line = lines[i].Trim();
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    metadata[key] = value;
                }
            }
        }

        // 返回 frontmatter 之后的内容
        var contentStartIndex = endIndex + 1;
        var content = string.Join('\n', lines.Skip(contentStartIndex));
        return (content, metadata);
    }
}
