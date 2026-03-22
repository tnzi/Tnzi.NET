namespace Tnzi.AI.Skills;

/// <summary>
/// File system skill store — scans SKILL.md files and delegates parsing to <see cref="SkillMarkdownParser"/>.
/// </summary>
/// <remarks>
/// Singleton lifetime with TTL caching (SemaphoreSlim double-checked locking).
/// Path discovery, caching, filtering, file I/O.
/// Parsing logic is in <see cref="SkillMarkdownParser"/>.
/// Requirements validation is in <see cref="ISkillRequirementsValidator"/>.
/// </remarks>
public class FileSystemSkillStore : ISkillStore
{
    private readonly ILogger<FileSystemSkillStore> _logger;
    private readonly SkillsOptions _options;
    private readonly string? _contentRootPath;
    private readonly ITnziApplication? _application;

    // 缓存状态
    private List<SkillDefinition>? _cache;
    private DateTime _cacheExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public FileSystemSkillStore(
        ILogger<FileSystemSkillStore> logger,
        IOptions<AIOptions> options,
        IHostEnvironment? hostEnvironment = null,
        ITnziApplication? application = null)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value.ContextProviders.Skills;
        _contentRootPath = hostEnvironment?.ContentRootPath;
        _application = application;
    }

    /// <inheritdoc/>
    public async Task<List<SkillDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        // 快速路径：缓存命中
        if (_cache != null && DateTime.UtcNow < _cacheExpiresAt)
            return _cache;

        await _cacheLock.WaitAsync(ct);
        try
        {
            // 双重检查
            if (_cache != null && DateTime.UtcNow < _cacheExpiresAt)
                return _cache;

            var skills = await LoadAllSkillsAsync(ct);
            _cache = skills;
            _cacheExpiresAt = DateTime.UtcNow + _options.CacheTtl;
            return skills;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<SkillDefinition?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Invalidates cache — next GetAllAsync call will reload from disk.
    /// </summary>
    public void InvalidateCache()
    {
        _cacheExpiresAt = DateTime.MinValue;
        _cache = null;
    }

    // -------------------------------------------------------------------------
    // Loading
    // -------------------------------------------------------------------------

    private async Task<List<SkillDefinition>> LoadAllSkillsAsync(CancellationToken ct)
    {
        var skills = new List<SkillDefinition>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Auto-discover module assembly Skills/ directories
        var autoDiscovered = DiscoverModuleSkillPaths();
        foreach (var path in autoDiscovered)
        {
            if (!scannedPaths.Add(path)) continue;
            try
            {
                var loaded = await LoadSkillsFromPathAsync(path, ct);
                skills.AddRange(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skills from auto-discovered path: {Path}", path);
            }
        }

        // 2. Configured paths (absolute, relative, @Assembly syntax)
        foreach (var path in _options.Paths)
        {
            var resolved = ResolvePath(path);
            if (resolved == null || !scannedPaths.Add(resolved)) continue;
            try
            {
                var loaded = await LoadSkillsFromPathAsync(resolved, ct);
                skills.AddRange(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skills from path: {Path} (resolved: {Resolved})", path, resolved);
            }
        }

        skills = FilterSkills(skills);

        _logger.LogInformation("Loaded {Count} skills from {PathCount} paths (auto-discovered: {AutoCount}, configured: {ConfigCount})",
            skills.Count, scannedPaths.Count, autoDiscovered.Count, _options.Paths.Count);

        return skills;
    }

    private async Task<List<SkillDefinition>> LoadSkillsFromPathAsync(string resolvedPath, CancellationToken ct)
    {
        var skills = new List<SkillDefinition>();

        if (!Directory.Exists(resolvedPath))
        {
            _logger.LogDebug("Skill path does not exist: {Path}", resolvedPath);
            return skills;
        }

        var skillFiles = Directory.GetFiles(resolvedPath, "SKILL.md", SearchOption.AllDirectories);

        foreach (var file in skillFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var skill = SkillMarkdownParser.Parse(content, file);
                if (skill != null)
                {
                    skills.Add(skill);
                    _logger.LogDebug("Loaded skill: {SkillName} from {FilePath}", skill.Name, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skill from file: {FilePath}", file);
            }
        }

        return skills;
    }

    // -------------------------------------------------------------------------
    // Path discovery
    // -------------------------------------------------------------------------

    private List<string> DiscoverModuleSkillPaths()
    {
        var paths = new List<string>();
        if (_application == null) return paths;

        foreach (var module in _application.Modules)
        {
            var assemblyLocation = module.Assembly.Location;
            if (string.IsNullOrEmpty(assemblyLocation)) continue;

            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (assemblyDir == null) continue;

            var skillsDir = Path.Combine(assemblyDir, "Skills");
            if (Directory.Exists(skillsDir))
            {
                paths.Add(skillsDir);
                _logger.LogDebug("Auto-discovered skill path from module {Module}: {Path}", module.Type.Name, skillsDir);
            }
        }

        if (_contentRootPath != null)
        {
            var appSkillsDir = Path.Combine(_contentRootPath, "Skills");
            if (Directory.Exists(appSkillsDir))
            {
                paths.Add(appSkillsDir);
                _logger.LogDebug("Auto-discovered skill path from ContentRoot: {Path}", appSkillsDir);
            }
        }

        return paths;
    }

    private string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        // @AssemblyName/subpath syntax
        if (path.StartsWith('@'))
        {
            var slashIndex = path.IndexOf('/');
            if (slashIndex < 0) slashIndex = path.IndexOf('\\');

            var assemblyName = slashIndex > 1 ? path[1..slashIndex] : path[1..];
            var subPath = slashIndex > 0 ? path[(slashIndex + 1)..] : "Skills";

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));

            if (assembly == null || string.IsNullOrEmpty(assembly.Location))
            {
                _logger.LogWarning("Cannot resolve @{AssemblyName} skill path: assembly not found or has no location", assemblyName);
                return null;
            }

            var assemblyDir = Path.GetDirectoryName(assembly.Location)!;
            return Path.Combine(assemblyDir, subPath);
        }

        if (Path.IsPathRooted(path))
            return path;

        return _contentRootPath != null ? Path.Combine(_contentRootPath, path) : path;
    }

    // -------------------------------------------------------------------------
    // Filtering
    // -------------------------------------------------------------------------

    private List<SkillDefinition> FilterSkills(List<SkillDefinition> skills)
    {
        if (_options.AllowList.Count > 0)
        {
            skills = skills.Where(s =>
                _options.AllowList.Contains(s.Name, StringComparer.OrdinalIgnoreCase) ||
                _options.AllowList.Contains(s.Slug, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (_options.DenyList.Count > 0)
        {
            skills = skills.Where(s =>
                !_options.DenyList.Contains(s.Name, StringComparer.OrdinalIgnoreCase) &&
                !_options.DenyList.Contains(s.Slug, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        return skills;
    }
}
