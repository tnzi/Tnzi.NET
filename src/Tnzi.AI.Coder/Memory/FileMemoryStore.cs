namespace Tnzi.AI.Coder.Memory;

/// <summary>
/// 文件系统记忆存储 — 每个 scope 对应一个 .md 文件
/// </summary>
public class FileMemoryStore : IMemoryStore
{
    private readonly CoderOptions _options;
    private readonly ILogger<FileMemoryStore> _logger;
    private readonly string _memoryDir;

    // 并发锁（按 scope）
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FileMemoryStore(IOptions<CoderOptions> options, ILogger<FileMemoryStore> logger)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);

        var projectRoot = Path.GetFullPath(_options.ProjectRoot);
        _memoryDir = Path.Combine(projectRoot, _options.MemoryDirectory);
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var filePath = GetScopeFilePath(scope);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var semaphore = GetLock(scope);
        await semaphore.WaitAsync(ct);
        try
        {
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
            _logger.LogDebug("Read memory scope '{Scope}': {Length} chars", scope, content.Length);
            return content;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string scope, string content, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNull(content);

        EnsureDirectoryExists();

        var filePath = GetScopeFilePath(scope);
        var semaphore = GetLock(scope);
        await semaphore.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, ct);
            _logger.LogDebug("Wrote memory scope '{Scope}': {Length} chars", scope, content.Length);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task AppendAsync(string scope, string entry, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNullOrWhiteSpace(entry);

        EnsureDirectoryExists();

        var filePath = GetScopeFilePath(scope);
        var semaphore = GetLock(scope);
        await semaphore.WaitAsync(ct);
        try
        {
            // 追加时添加换行分隔
            var appendContent = File.Exists(filePath) ? $"\n{entry}" : entry;
            await File.AppendAllTextAsync(filePath, appendContent, Encoding.UTF8, ct);
            _logger.LogDebug("Appended to memory scope '{Scope}': {Length} chars", scope, entry.Length);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string scope, string query, int maxResults = 10, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);
        Check.NotNullOrWhiteSpace(query);

        var content = await ReadAsync(scope, ct);
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        // 按段落分割
        var paragraphs = SplitIntoParagraphs(content);
        var nonEmptyParagraphs = paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (nonEmptyParagraphs.Count == 0)
        {
            return [];
        }

        var keywords = Tokenize(query);
        if (keywords.Length == 0)
        {
            return [];
        }

        // 对每个段落进行分词
        var paragraphTokens = nonEmptyParagraphs.Select(Tokenize).ToList();
        var totalParagraphs = nonEmptyParagraphs.Count;

        // 计算 IDF: log(总段落数 / 包含该词的段落数)
        var idf = new Dictionary<string, double>();
        foreach (var keyword in keywords)
        {
            var containingCount = paragraphTokens.Count(tokens => tokens.Contains(keyword));
            // 包含该词的段落数为 0 时，IDF 设为 0（该词不在任何段落中）
            idf[keyword] = containingCount > 0
                ? Math.Log(1.0 + (double)totalParagraphs / containingCount)
                : 0;
        }

        // 计算每个段落的 TF-IDF 分数
        var results = new List<MemorySearchResult>();
        var maxScore = 0.0;

        for (var i = 0; i < nonEmptyParagraphs.Count; i++)
        {
            var tokens = paragraphTokens[i];
            if (tokens.Length == 0)
            {
                continue;
            }

            // 统计词频
            var termCounts = new Dictionary<string, int>();
            foreach (var token in tokens)
            {
                termCounts[token] = termCounts.GetValueOrDefault(token) + 1;
            }

            // 计算 TF-IDF 得分
            var score = 0.0;
            var matchedKeywords = 0;

            foreach (var keyword in keywords)
            {
                if (!termCounts.TryGetValue(keyword, out var count))
                {
                    continue;
                }

                // TF = 词频 / 段落总词数
                var tf = (double)count / tokens.Length;
                score += tf * idf[keyword];
                matchedKeywords++;
            }

            if (score <= 0)
            {
                continue;
            }

            // 所有查询词都出现时，加 0.2 的短语相近度加成
            if (matchedKeywords == keywords.Length && keywords.Length > 1)
            {
                score += 0.2;
            }

            if (score > maxScore)
            {
                maxScore = score;
            }

            results.Add(new MemorySearchResult
            {
                Content = nonEmptyParagraphs[i].Trim(),
                Source = scope,
                Score = score
            });
        }

        // 归一化分数到 0-1 范围
        if (maxScore > 0)
        {
            foreach (var result in results)
            {
                result.Score = result.Score / maxScore;
            }
        }

        _logger.LogDebug("Searched memory scope '{Scope}' for '{Query}': {Count} results",
            scope, query, results.Count);

        return results
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// 文本分词 — 小写化、去除标点、按空白分割
    /// </summary>
    private static string[] Tokenize(string text)
    {
        var lower = text.ToLower();
        var sb = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : ' ');
        }

        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        var filePath = GetScopeFilePath(scope);
        var semaphore = GetLock(scope);
        await semaphore.WaitAsync(ct);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Cleared memory scope '{Scope}'", scope);
            }
        }
        finally
        {
            semaphore.Release();
        }

        // 清理锁条目，防止长期运行时锁字典无限增长
        if (_locks.TryRemove(scope, out var removed))
        {
            removed.Dispose();
        }
    }

    /// <summary>
    /// 获取 scope 对应的文件路径（含路径遍历防护）
    /// </summary>
    private string GetScopeFilePath(string scope)
    {
        // 防止路径遍历攻击
        if (scope.Contains("..") || scope.Contains('/') || scope.Contains('\\'))
        {
            throw new ArgumentException($"Invalid scope name: '{scope}'. Must not contain path separators or '..'.");
        }

        // default scope → MEMORY.md，其他 scope → {scope}.md
        var fileName = scope.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? "MEMORY.md"
            : $"{SanitizeFileName(scope)}.md";

        var filePath = Path.Combine(_memoryDir, fileName);

        // 二次验证：确保解析后的路径仍在 _memoryDir 内
        var resolvedPath = Path.GetFullPath(filePath);
        var resolvedDir = Path.GetFullPath(_memoryDir);
        if (!resolvedPath.StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid scope name: '{scope}'. Resolved path is outside memory directory.");
        }

        return filePath;
    }

    /// <summary>
    /// 清理文件名中的非法字符
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sanitized.Append(invalidChars.Contains(c) ? '_' : c);
        }
        return sanitized.ToString();
    }

    /// <summary>
    /// 将内容按段落分割
    /// </summary>
    private static List<string> SplitIntoParagraphs(string content)
    {
        // 按 Markdown 标题（## ）或双换行或 --- 分隔
        var paragraphs = new List<string>();
        var currentParagraph = new StringBuilder();

        foreach (var line in content.Split('\n'))
        {
            // 以 ## 开头的标题行作为新段落开始
            if (line.TrimStart().StartsWith("## ") && currentParagraph.Length > 0)
            {
                paragraphs.Add(currentParagraph.ToString());
                currentParagraph.Clear();
            }

            // --- 分隔符也作为段落分割
            if (line.Trim() == "---" && currentParagraph.Length > 0)
            {
                paragraphs.Add(currentParagraph.ToString());
                currentParagraph.Clear();
                continue;
            }

            currentParagraph.AppendLine(line);
        }

        if (currentParagraph.Length > 0)
        {
            paragraphs.Add(currentParagraph.ToString());
        }

        return paragraphs;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_memoryDir))
        {
            Directory.CreateDirectory(_memoryDir);
        }
    }

    private SemaphoreSlim GetLock(string scope)
    {
        return _locks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));
    }
}
