using Tnzi.AI;

namespace Tnzi.AI.Sandbox.Middleware;

public static class SandboxPropertyKeys
{
    public const string ThreadData = "ThreadData";
    public const string Sandbox = "Sandbox";
    public const string SandboxId = "SandboxId";
}

public record ThreadDataState(string ThreadDirectory, string WorkspacePath, string UploadsPath, string OutputsPath, string SkillsPath);

public class ThreadDataMiddleware : IAiMiddleware
{
    private const string ExtractedMarker = ".extracted";

    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<ThreadDataMiddleware> _logger;
    private readonly ISkillStore? _skillStore;

    public int Order => AiMiddlewareOrders.ThreadData;

    public ThreadDataMiddleware(
        IOptions<SandboxModuleOptions> options,
        IVirtualPathTranslator translator,
        ILogger<ThreadDataMiddleware> logger,
        ISkillStore? skillStore = null)
    {
        _options = Check.NotNull(options);
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
        _skillStore = skillStore;
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        await SetupThreadDataAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await SetupThreadDataAsync(context, cancellationToken);
        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }

    private async Task SetupThreadDataAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        if (context.ShouldSkipMiddleware) return;

        var threadId = context.Request.ThreadId;
        if (threadId is null) return;

        var threadDir = _translator.GetThreadDirectory(threadId.Value);
        var state = new ThreadDataState(
            ThreadDirectory: threadDir,
            WorkspacePath: Path.Combine(threadDir, "workspace"),
            UploadsPath: Path.Combine(threadDir, "uploads"),
            OutputsPath: Path.Combine(threadDir, "outputs"),
            SkillsPath: Path.Combine(threadDir, "skills"));

        if (!_options.Value.LazyDirectoryCreation)
        {
            _translator.EnsureThreadDirectories(threadId.Value);
            _logger.LogDebug("Created thread directories for {ThreadId}", threadId);
        }

        await ExtractSkillResourcesAsync(state.SkillsPath, ct);

        context.Properties[SandboxPropertyKeys.ThreadData] = state;
    }

    /// <summary>
    /// 将技能的附属资源提取到线程 skills/ 目录。
    /// 使用 .extracted 标记文件判断是否已完成提取（目录存在但标记缺失 = 上次失败，需重试）。
    /// </summary>
    private async Task ExtractSkillResourcesAsync(string skillsPath, CancellationToken ct)
    {
        if (_skillStore == null) return;

        var markerPath = Path.Combine(skillsPath, ExtractedMarker);
        if (File.Exists(markerPath)) return; // 已成功提取过

        try
        {
            var skills = await _skillStore.GetAllAsync(ct);
            var extractedCount = 0;

            foreach (var skill in skills)
            {
                if (skill.Resources.Count == 0) continue;

                var skillDir = Path.Combine(skillsPath, skill.Slug);
                foreach (var (relativePath, content) in skill.Resources)
                {
                    var filePath = Path.Combine(skillDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(filePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(filePath, content, ct);
                    extractedCount++;
                }
            }

            // 写入标记文件表示提取完成
            Directory.CreateDirectory(skillsPath);
            await File.WriteAllTextAsync(markerPath, $"{extractedCount} files extracted", ct);

            if (extractedCount > 0)
                _logger.LogDebug("Extracted {Count} skill resource files to {Path}", extractedCount, skillsPath);
        }
        catch (Exception ex)
        {
            // 提取失败时删除不完整的目录，允许下次重试
            try { if (Directory.Exists(skillsPath)) Directory.Delete(skillsPath, recursive: true); }
            catch { /* best effort cleanup */ }

            _logger.LogWarning(ex, "Failed to extract skill resources to {Path}", skillsPath);
        }
    }
}
