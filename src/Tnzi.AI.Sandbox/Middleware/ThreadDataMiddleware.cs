namespace Tnzi.AI.Sandbox.Middleware;

public class ThreadDataMiddleware : IAiMiddleware
{
    /// <summary>
    /// Sentinel file written into the THREAD directory (not into the skills/
    /// subdirectory) once the thread's skill resources are wired up. Living
    /// in the thread directory means the sentinel works for both modes:
    /// symlink (skills/ points at a shared root we must not pollute) and copy
    /// (skills/ is a private directory).
    /// </summary>
    private const string SkillsWiredMarker = ".skills_wired";

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
    /// Wires this thread's <c>skills/</c> directory up to the available skill
    /// resources. Prefers a symbolic link into the shared <c>_skills/</c> root
    /// populated once at application startup by
    /// <see cref="SharedSkillExtractor"/>; falls back to a per-thread copy
    /// when symlinks are unavailable (e.g. Windows without developer mode).
    /// </summary>
    /// <remarks>
    /// Idempotent via the <see cref="SkillsWiredMarker"/> sentinel in the
    /// parent thread directory - a thread is wired exactly once. The marker
    /// is written only on success so a half-completed wiring is retried on
    /// the next request.
    /// </remarks>
    private async Task ExtractSkillResourcesAsync(string skillsPath, CancellationToken ct)
    {
        if (_skillStore == null) return;

        var threadDir = Path.GetDirectoryName(skillsPath);
        if (threadDir is null) return;

        // Sentinel lives in the parent thread directory rather than inside
        // skillsPath, because skillsPath may end up as a symlink to the shared
        // root - writing a file inside the link would pollute the shared dir.
        var marker = Path.Combine(threadDir, SkillsWiredMarker);
        if (File.Exists(marker)) return;

        // Path 1 (preferred): symlink skills/ to the shared root populated at
        // startup. Threads pay zero IO; the read-only file attribute set by
        // SharedSkillExtractor stops agents from poisoning the shared content.
        var dataRoot = _options.Value.DataRoot;
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            var sharedRoot = SharedSkillExtractor.GetSharedRoot(dataRoot);
            var sharedMarker = Path.Combine(sharedRoot, SharedSkillExtractor.ExtractedMarker);
            if (File.Exists(sharedMarker) && TryLinkToShared(skillsPath, sharedRoot))
            {
                await File.WriteAllTextAsync(marker, $"linked->{sharedRoot} at {DateTime.UtcNow:O}", ct);
                _logger.LogDebug("Linked thread skills dir to shared root {Path}", sharedRoot);
                return;
            }
        }

        // Path 2 (fallback): per-thread copy of every skill resource.
        if (await CopyAllSkillResourcesAsync(skillsPath, ct))
        {
            await File.WriteAllTextAsync(marker, $"copied at {DateTime.UtcNow:O}", ct);
        }
    }

    /// <summary>
    /// Replaces <paramref name="skillsPath"/> with a symbolic link to
    /// <paramref name="sharedRoot"/>. Returns <c>true</c> only when the link
    /// was actually created. Caller is responsible for writing the
    /// completion marker outside the link target.
    /// </summary>
    private bool TryLinkToShared(string skillsPath, string sharedRoot)
    {
        try
        {
            // CreateSymbolicLink refuses an existing target. Drop an empty stub
            // directory created by EnsureThreadDirectories, but never replace
            // a non-empty directory (a previous copy-fallback run left files).
            if (Directory.Exists(skillsPath))
            {
                if (Directory.EnumerateFileSystemEntries(skillsPath).Any())
                    return false;
                Directory.Delete(skillsPath);
            }

            Directory.CreateSymbolicLink(skillsPath, sharedRoot);
            return true;
        }
        catch (Exception ex)
        {
            // Windows non-admin without developer mode: UnauthorizedAccessException.
            // Filesystem doesn't support symlinks: IOException.
            // Either case we silently fall back to copy.
            _logger.LogDebug(ex, "Symlink unavailable for {Path}, falling back to copy", skillsPath);
            return false;
        }
    }

    /// <summary>
    /// Copies every loaded skill's resource files into <paramref name="skillsPath"/>.
    /// Returns <c>true</c> on success; the caller writes the completion marker.
    /// </summary>
    private async Task<bool> CopyAllSkillResourcesAsync(string skillsPath, CancellationToken ct)
    {
        try
        {
            var skills = await _skillStore!.GetAllAsync(ct);
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

            Directory.CreateDirectory(skillsPath);

            if (extractedCount > 0)
                _logger.LogDebug("Copied {Count} skill resource files to {Path}", extractedCount, skillsPath);

            return true;
        }
        catch (Exception ex)
        {
            // Failed extraction: remove the partial directory so the next request retries.
            try { if (Directory.Exists(skillsPath)) Directory.Delete(skillsPath, recursive: true); }
            catch { /* best effort cleanup */ }

            _logger.LogWarning(ex, "Failed to extract skill resources to {Path}", skillsPath);
            return false;
        }
    }
}
