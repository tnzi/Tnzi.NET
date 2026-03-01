namespace Tnzi.AI.Infrastructure.Conversation;

/// <summary>
/// 文件系统对话存储 — 每个对话对应一个 JSON 文件，索引文件维护摘要列表
/// </summary>
public class FileConversationStore : IConversationStore
{
    private readonly FileConversationStoreOptions _options;
    private readonly ILogger<FileConversationStore> _logger;
    private readonly string _conversationDir;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public FileConversationStore(IOptions<FileConversationStoreOptions> options, ILogger<FileConversationStore> logger)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);

        var projectRoot = Path.GetFullPath(_options.ProjectRoot);
        _conversationDir = Path.Combine(projectRoot, _options.ConversationDirectory);
    }

    /// <inheritdoc />
    public async Task<ConversationContext> GetOrCreateAsync(string conversationId, Guid? agentId = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);

        EnsureDirectoryExists();

        var filePath = GetConversationFilePath(conversationId);

        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
            var context = ConversationContext.Deserialize(json);
            if (context != null)
            {
                _logger.LogDebug("Loaded conversation '{ConversationId}': {MessageCount} messages",
                    conversationId, context.Messages.Count);
                return context;
            }
        }

        // 创建新对话
        var newContext = new ConversationContext();
        _logger.LogDebug("Created new conversation '{ConversationId}'", conversationId);

        // 更新索引
        await UpdateIndexAsync(conversationId, newContext, ct);

        return newContext;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string conversationId, ConversationContext context, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);
        Check.NotNull(context);

        EnsureDirectoryExists();

        var filePath = GetConversationFilePath(conversationId);
        var json = context.Serialize();
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, ct);

        // 更新索引
        await UpdateIndexAsync(conversationId, context, ct);

        _logger.LogDebug("Saved conversation '{ConversationId}': {MessageCount} messages",
            conversationId, context.Messages.Count);
    }

    /// <inheritdoc />
    public async Task AppendMessageAsync(string conversationId, string role, string content, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);
        Check.NotNullOrWhiteSpace(role);
        Check.NotNullOrWhiteSpace(content);

        var context = await GetOrCreateAsync(conversationId, ct: ct);

        var chatRole = role.ToLower() switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        context.Messages.Add(new ChatMessage(chatRole, content));
        await SaveAsync(conversationId, context, ct);

        _logger.LogDebug("Appended {Role} message to conversation '{ConversationId}'", role, conversationId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        EnsureDirectoryExists();

        var index = await LoadIndexAsync(ct);

        return index
            .OrderByDescending(s => s.LastActivity)
            .Take(limit)
            .ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);

        var filePath = GetConversationFilePath(conversationId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        // 从索引移除
        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var updated = index.Where(s => s.Id != conversationId).ToList();
            await SaveIndexAsync(updated, ct);
        }
        finally
        {
            _indexLock.Release();
        }

        _logger.LogDebug("Deleted conversation '{ConversationId}'", conversationId);
    }

    /// <summary>
    /// 获取对话文件路径（含路径遍历防护）
    /// </summary>
    private string GetConversationFilePath(string conversationId)
    {
        // 防止路径遍历攻击
        if (conversationId.Contains("..") || conversationId.Contains('/') || conversationId.Contains('\\'))
        {
            throw new ArgumentException($"Invalid conversation ID: '{conversationId}'. Must not contain path separators or '..'.");
        }

        var filePath = Path.Combine(_conversationDir, $"{SanitizeId(conversationId)}.json");

        // 二次验证：确保解析后的路径仍在 _conversationDir 内
        var resolvedPath = Path.GetFullPath(filePath);
        var resolvedDir = Path.GetFullPath(_conversationDir);
        if (!resolvedPath.StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid conversation ID: '{conversationId}'. Resolved path is outside conversation directory.");
        }

        return filePath;
    }

    /// <summary>
    /// 获取索引文件路径
    /// </summary>
    private string GetIndexFilePath()
    {
        return Path.Combine(_conversationDir, "index.json");
    }

    /// <summary>
    /// 更新索引中的对话摘要
    /// </summary>
    private async Task UpdateIndexAsync(string conversationId, ConversationContext context, CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            var index = await LoadIndexAsync(ct);
            var existing = index.FirstOrDefault(s => s.Id == conversationId);

            // 生成标题：取第一条 user 消息的前 50 个字符
            var title = context.Messages
                .FirstOrDefault(m => m.Role == ChatRole.User)?.Text;
            if (title != null && title.Length > 50)
            {
                title = title[..50] + "...";
            }

            if (existing != null)
            {
                existing.Title = title ?? existing.Title;
                existing.LastActivity = DateTime.UtcNow;
                existing.MessageCount = context.Messages.Count;
            }
            else
            {
                index.Add(new ConversationSummary
                {
                    Id = conversationId,
                    Title = title,
                    LastActivity = DateTime.UtcNow,
                    MessageCount = context.Messages.Count
                });
            }

            await SaveIndexAsync(index, ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// 加载索引
    /// </summary>
    private async Task<List<ConversationSummary>> LoadIndexAsync(CancellationToken ct)
    {
        var indexPath = GetIndexFilePath();
        if (!File.Exists(indexPath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(indexPath, Encoding.UTF8, ct);
            return JsonSerializer.Deserialize<List<ConversationSummary>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            _logger.LogDebug("Failed to parse conversation index, creating new");
            return [];
        }
    }

    /// <summary>
    /// 保存索引
    /// </summary>
    private async Task SaveIndexAsync(List<ConversationSummary> index, CancellationToken ct)
    {
        var indexPath = GetIndexFilePath();
        var json = JsonSerializer.Serialize(index, JsonOptions);
        await File.WriteAllTextAsync(indexPath, json, Encoding.UTF8, ct);
    }

    /// <summary>
    /// 清理 ID 中的非法字符
    /// </summary>
    private static string SanitizeId(string id)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            sanitized.Append(invalidChars.Contains(c) ? '_' : c);
        }
        return sanitized.ToString();
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_conversationDir))
        {
            Directory.CreateDirectory(_conversationDir);
        }
    }
}
