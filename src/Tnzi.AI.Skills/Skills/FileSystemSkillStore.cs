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
public class FileSystemSkillStore : ISkillStore, IDisposable
{
    private readonly ILogger<FileSystemSkillStore> _logger;
    private readonly SkillsOptions _options;
    private readonly string? _contentRootPath;
    private readonly ITnziApplication? _application;

    // 缓存状态
    private List<SkillDefinition>? _cache;
    private DateTime _cacheExpiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // 项目目录路径（来自 ContentRoot/Skills 等），用于标记 SkillSource.Project
    private readonly HashSet<string> _projectPaths = new(StringComparer.OrdinalIgnoreCase);

    // OptionsMonitor 订阅 — SkillsOptions 变化时立即失效缓存
    private readonly IDisposable? _optionsChangeListener;

    public FileSystemSkillStore(
        ILogger<FileSystemSkillStore> logger,
        IOptions<AIOptions> options,
        IHostEnvironment? hostEnvironment = null,
        ITnziApplication? application = null,
        IOptionsMonitor<AIOptions>? optionsMonitor = null)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value.ContextProviders.Skills;
        _contentRootPath = hostEnvironment?.ContentRootPath;
        _application = application;
        _optionsChangeListener = optionsMonitor?.OnChange(_ => InvalidateCache());
    }

    public void Dispose()
    {
        _optionsChangeListener?.Dispose();
        _cacheLock.Dispose();
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
        var scannedSkillFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 0. Load built-in skills from EmbeddedResource (lowest priority, overridden by file system)
        var builtInCount = _options.LoadBuiltIn ? LoadBuiltInSkills(skills) : 0;

        // 1. DataPath — tenant/user installed skills ({DataPath}/skills/{tenantId}/{userId}/)
        var dataPathCount = await LoadFromDataPathAsync(skills, scannedPaths, ct);

        // 2. Auto-discover module assembly Skills/ directories
        var autoDiscovered = DiscoverModuleSkillPaths();
        foreach (var path in autoDiscovered)
        {
            var normalized = NormalizeDirectoryPath(path);
            if (normalized == null || !scannedPaths.Add(normalized)) continue;
            try
            {
                var loaded = await LoadSkillsFromPathAsync(normalized, scannedSkillFiles, ct);
                skills.AddRange(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skills from auto-discovered path: {Path}", normalized);
            }
        }

        // 3. Configured paths (absolute, relative, @Assembly syntax)
        foreach (var path in _options.Paths)
        {
            var resolved = ResolvePath(path);
            var normalized = NormalizeDirectoryPath(resolved);
            if (normalized == null || !scannedPaths.Add(normalized)) continue;
            try
            {
                var loaded = await LoadSkillsFromPathAsync(normalized, scannedSkillFiles, ct);
                skills.AddRange(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skills from path: {Path} (resolved: {Resolved})", path, normalized);
            }
        }

        // 4. Plugin paths — third-party plugin skills (SkillSource.Plugin)
        var pluginCount = await LoadSourcedPathsAsync(
            _options.PluginPaths, SkillSource.Plugin, skills, scannedPaths, scannedSkillFiles, ct);

        // 5. Managed paths — platform-managed skills (SkillSource.Managed)
        var managedCount = await LoadSourcedPathsAsync(
            _options.ManagedPaths, SkillSource.Managed, skills, scannedPaths, scannedSkillFiles, ct);

        skills = FilterSkills(skills);

        _logger.LogInformation(
            "Loaded {Count} skills (built-in: {BuiltIn}, plugin: {Plugin}, managed: {Managed}, file-system: {FileSystem})",
            skills.Count, builtInCount, pluginCount, managedCount,
            skills.Count - builtInCount - pluginCount - managedCount);

        return skills;
    }

    /// <summary>
    /// Loads skills from a list of paths and assigns the specified <see cref="SkillSource"/>.
    /// </summary>
    private async Task<int> LoadSourcedPathsAsync(
        List<string> paths, SkillSource source,
        List<SkillDefinition> skills, HashSet<string> scannedPaths, HashSet<string> scannedSkillFiles,
        CancellationToken ct)
    {
        if (paths.Count == 0) return 0;

        var count = 0;
        foreach (var path in paths)
        {
            var resolved = ResolvePath(path);
            var normalized = NormalizeDirectoryPath(resolved);
            if (normalized == null || !scannedPaths.Add(normalized)) continue;
            try
            {
                var loaded = await LoadSkillsFromPathAsync(normalized, scannedSkillFiles, ct);
                foreach (var skill in loaded)
                {
                    skill.Source = source;
                }

                skills.AddRange(loaded);
                count += loaded.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load {Source} skills from path: {Path} (resolved: {Resolved})",
                    source, path, normalized);
            }
        }

        if (count > 0)
            _logger.LogDebug("Loaded {Count} {Source} skills", count, source);

        return count;
    }

    private async Task<List<SkillDefinition>> LoadSkillsFromPathAsync(string resolvedPath, HashSet<string> scannedSkillFiles, CancellationToken ct)
    {
        var skills = new List<SkillDefinition>();

        if (!Directory.Exists(resolvedPath))
        {
            _logger.LogDebug("Skill path does not exist: {Path}", resolvedPath);
            return skills;
        }

        var ignoreMatcher = GitIgnoreRuleSet.Load(resolvedPath);
        var skillFiles = Directory.EnumerateFiles(resolvedPath, "SKILL.md", SearchOption.AllDirectories);

        foreach (var file in skillFiles)
        {
            ct.ThrowIfCancellationRequested();

            var normalizedFile = NormalizeFilePath(file);
            if (normalizedFile == null || !scannedSkillFiles.Add(normalizedFile))
            {
                continue;
            }

            if (ignoreMatcher.IsIgnored(normalizedFile))
            {
                _logger.LogDebug("Skipped gitignored skill file: {FilePath}", normalizedFile);
                continue;
            }

            try
            {
                var content = await File.ReadAllTextAsync(normalizedFile, ct);
                var skill = SkillMarkdownParser.Parse(content, normalizedFile);
                if (skill != null)
                {
                    // 标记来自项目目录的技能为 Project 来源
                    if (IsProjectPath(resolvedPath))
                    {
                        skill.Source = SkillSource.Project;
                    }

                    var skillDir = Path.GetDirectoryName(normalizedFile)!;
                    await LoadResourcesFromDirectoryAsync(skill, skillDir, ct);
                    skills.Add(skill);
                    _logger.LogDebug("Loaded skill: {SkillName} from {FilePath}", skill.Name, normalizedFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skill from file: {FilePath}", normalizedFile);
            }
        }

        return skills;
    }

    // -------------------------------------------------------------------------
    // Path discovery
    // -------------------------------------------------------------------------

    /// <summary>
    /// 从 DataPath 加载租户/用户级技能。
    /// 目录结构：{DataPath}/skills/{tenantId}/ → Tenant scope，{DataPath}/skills/{tenantId}/{userId}/ → User scope。
    /// 直接在 {DataPath}/skills/ 下的技能为全局（System scope）。
    /// </summary>
    private async Task<int> LoadFromDataPathAsync(
        List<SkillDefinition> skills, HashSet<string> scannedPaths, CancellationToken ct)
    {
        var dataPath = _options.DataPath;
        if (string.IsNullOrWhiteSpace(dataPath)) return 0;

        // 支持相对路径（相对于 ContentRoot）
        if (!Path.IsPathRooted(dataPath) && _contentRootPath != null)
            dataPath = Path.Combine(_contentRootPath, dataPath);

        var skillsRoot = NormalizeDirectoryPath(Path.Combine(dataPath, "skills"));
        if (skillsRoot == null)
        {
            return 0;
        }

        if (!Directory.Exists(skillsRoot)) return 0;
        if (!scannedPaths.Add(skillsRoot)) return 0;

        var count = 0;

        // 直接子目录（非 tenants/users）→ System scope
        foreach (var topDir in Directory.GetDirectories(skillsRoot))
        {
            ct.ThrowIfCancellationRequested();
            var topName = Path.GetFileName(topDir);
            if (topName is "tenants" or "users") continue;

            if (File.Exists(Path.Combine(topDir, "SKILL.md")))
                count += await LoadScopedSkillAsync(skills, topDir, SkillScope.System, null, null, ct);
        }

        // tenants/{tenantId}/{skill-slug}/ → Tenant scope
        // tenants/{tenantId}/users/{userId}/{skill-slug}/ → User scope（多租户隔离）
        var tenantsDir = Path.Combine(skillsRoot, "tenants");
        if (Directory.Exists(tenantsDir))
        {
            foreach (var tenantDir in Directory.GetDirectories(tenantsDir))
            {
                ct.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(tenantDir), out var tenantId)) continue;

                foreach (var subDir in Directory.GetDirectories(tenantDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var subName = Path.GetFileName(subDir);

                    // tenants/{tenantId}/users/{userId}/{skill-slug}/ → User scope with tenant isolation
                    if (string.Equals(subName, "users", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var userDir in Directory.GetDirectories(subDir))
                        {
                            if (!Guid.TryParse(Path.GetFileName(userDir), out var tenantUserId)) continue;
                            foreach (var skillDir in Directory.GetDirectories(userDir))
                            {
                                ct.ThrowIfCancellationRequested();
                                if (File.Exists(Path.Combine(skillDir, "SKILL.md")))
                                    count += await LoadScopedSkillAsync(skills, skillDir, SkillScope.User, tenantId, tenantUserId, ct);
                            }
                        }
                        continue;
                    }

                    // tenants/{tenantId}/{skill-slug}/ → Tenant scope
                    if (File.Exists(Path.Combine(subDir, "SKILL.md")))
                        count += await LoadScopedSkillAsync(skills, subDir, SkillScope.Tenant, tenantId, null, ct);
                }
            }
        }

        // users/{userId}/{skill-slug}/ → User scope（单租户 / 无租户场景）
        var usersDir = Path.Combine(skillsRoot, "users");
        if (Directory.Exists(usersDir))
        {
            foreach (var userDir in Directory.GetDirectories(usersDir))
            {
                ct.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(userDir), out var userId)) continue;

                foreach (var skillDir in Directory.GetDirectories(userDir))
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(Path.Combine(skillDir, "SKILL.md")))
                        count += await LoadScopedSkillAsync(skills, skillDir, SkillScope.User, null, userId, ct);
                }
            }
        }

        if (count > 0)
            _logger.LogDebug("Loaded {Count} installed skills from DataPath: {Path}", count, skillsRoot);

        return count;
    }

    private async Task<int> LoadScopedSkillAsync(
        List<SkillDefinition> skills, string skillDir,
        SkillScope scope, Guid? tenantId, Guid? userId,
        CancellationToken ct)
    {
        try
        {
            var skillMdPath = NormalizeFilePath(Path.Combine(skillDir, "SKILL.md"));
            if (skillMdPath == null)
            {
                return 0;
            }

            var content = await File.ReadAllTextAsync(skillMdPath, ct);
            var skill = SkillMarkdownParser.Parse(content, skillMdPath);
            if (skill == null) return 0;

            skill.Scope = scope;
            skill.TenantId = tenantId;
            skill.OwnerUserId = userId;

            await LoadResourcesFromDirectoryAsync(skill, skillDir, ct);
            skills.Add(skill);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skill from {Path}", skillDir);
            return 0;
        }
    }

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
                _projectPaths.Add(NormalizeDirectoryPath(appSkillsDir) ?? appSkillsDir);
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
            return NormalizeDirectoryPath(Path.Combine(assemblyDir, subPath));
        }

        if (Path.IsPathRooted(path))
            return NormalizeDirectoryPath(path);

        return NormalizeDirectoryPath(_contentRootPath != null ? Path.Combine(_contentRootPath, path) : path);
    }

    // -------------------------------------------------------------------------
    // Built-in skills (EmbeddedResource)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 从程序集嵌入资源加载内置技能（SKILL.md + 附属资源）。返回加载数量。
    /// 内置技能优先级最低 — 文件系统中同 slug 的技能会在后续合并时覆盖。
    /// </summary>
    /// <remarks>
    /// LogicalName 格式：<c>BuiltInSkill:{slug}/{relativePath}</c>，例如：
    /// <code>
    /// BuiltInSkill:deep-research/SKILL.md
    /// BuiltInSkill:chart-visualization/references/generate_bar_chart.md
    /// BuiltInSkill:data-analysis/scripts/analyze.py
    /// </code>
    /// 按 slug（路径第一段）分组，同 slug 的 SKILL.md 为主文件，其余为附属资源。
    /// </remarks>
    private int LoadBuiltInSkills(List<SkillDefinition> skills)
    {
        var assembly = typeof(FileSystemSkillStore).Assembly;
        const string prefix = "BuiltInSkill:";

        var allResources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        if (allResources.Count == 0) return 0;

        // %(RecursiveDir) 在 Windows 上使用反斜杠，统一替换为正斜杠用于路径处理
        // 保留原始资源名用于 ReadResource
        // 按 slug 分组（路径的第一段）
        var skillGroups = allResources
            .Select(n => (ResourceName: n, RelativePath: n[prefix.Length..].Replace('\\', '/')))
            .GroupBy(r =>
            {
                var slashIndex = r.RelativePath.IndexOf('/');
                return slashIndex > 0 ? r.RelativePath[..slashIndex] : r.RelativePath;
            })
            .ToList();

        var count = 0;
        foreach (var group in skillGroups)
        {
            var slug = group.Key;
            var skillMdEntry = group.FirstOrDefault(r =>
                r.RelativePath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.RelativePath, "SKILL.md", StringComparison.OrdinalIgnoreCase));

            if (skillMdEntry.ResourceName == null) continue;

            try
            {
                var content = ReadResource(assembly, skillMdEntry.ResourceName);
                if (content == null) continue;

                var skill = SkillMarkdownParser.Parse(content, skillMdEntry.ResourceName);
                if (skill == null) continue;

                // 加载附属资源（scripts, references, templates 等）
                foreach (var res in group)
                {
                    if (res.ResourceName == skillMdEntry.ResourceName) continue;

                    var resContent = ReadResource(assembly, res.ResourceName);
                    if (resContent == null) continue;

                    // 去掉 slug/ 前缀，得到相对路径如 "scripts/generate.py"
                    var relativePath = res.RelativePath[(slug.Length + 1)..];
                    skill.Resources[relativePath] = resContent;
                }

                skills.Add(skill);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load built-in skill: {Slug}", slug);
            }
        }

        if (count > 0)
            _logger.LogDebug("Loaded {Count} built-in skills from embedded resources", count);

        return count;
    }

    private static string? ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 从技能目录加载附属资源文件（scripts/, references/, templates/, agents/, assets/）
    /// </summary>
    private static async Task LoadResourcesFromDirectoryAsync(
        SkillDefinition skill, string skillDir, CancellationToken ct)
    {
        foreach (var subDir in new[] { "scripts", "references", "templates", "agents", "assets" })
        {
            var fullDir = Path.Combine(skillDir, subDir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var resourceFile in Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var relPath = Path.GetRelativePath(skillDir, resourceFile).Replace('\\', '/');
                    skill.Resources[relPath] = await File.ReadAllTextAsync(resourceFile, ct);
                }
                catch (Exception) { /* skip unreadable files */ }
            }
        }
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

    private static string? NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private bool IsProjectPath(string resolvedPath)
    {
        return _projectPaths.Any(pp =>
            resolvedPath.StartsWith(pp, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private sealed class GitIgnoreRuleSet
    {
        private readonly string _rootPath;
        private readonly List<GitIgnoreRule> _rules;

        private GitIgnoreRuleSet(string rootPath, List<GitIgnoreRule> rules)
        {
            _rootPath = rootPath;
            _rules = rules;
        }

        public static GitIgnoreRuleSet Load(string rootPath)
        {
            var normalizedRoot = NormalizeDirectoryPath(rootPath) ?? rootPath;
            var rules = new List<GitIgnoreRule>();
            foreach (var gitIgnorePath in Directory.EnumerateFiles(normalizedRoot, ".gitignore", SearchOption.AllDirectories)
                         .OrderBy(x => x.Length))
            {
                var baseDirectory = Path.GetDirectoryName(gitIgnorePath);
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadLines(gitIgnorePath))
                {
                    var rule = GitIgnoreRule.TryParse(baseDirectory, rawLine);
                    if (rule != null)
                    {
                        rules.Add(rule);
                    }
                }
            }

            return new GitIgnoreRuleSet(normalizedRoot, rules);
        }

        public bool IsIgnored(string filePath)
        {
            var normalizedFile = NormalizeFilePath(filePath);
            if (normalizedFile == null || !normalizedFile.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var ignored = false;
            foreach (var rule in _rules)
            {
                if (rule.IsMatch(normalizedFile))
                {
                    ignored = !rule.IsNegated;
                }
            }

            return ignored;
        }
    }

    private sealed class GitIgnoreRule
    {
        private GitIgnoreRule(string baseDirectory, string pattern, bool isNegated, bool directoryOnly)
        {
            BaseDirectory = baseDirectory;
            Pattern = pattern;
            IsNegated = isNegated;
            DirectoryOnly = directoryOnly;
            BasenameOnly = !pattern.Contains('/');
            Regex = BuildRegex(pattern, directoryOnly, BasenameOnly);
        }

        public string BaseDirectory { get; }

        public string Pattern { get; }

        public bool IsNegated { get; }

        public bool DirectoryOnly { get; }

        public bool BasenameOnly { get; }

        public Regex Regex { get; }

        public static GitIgnoreRule? TryParse(string baseDirectory, string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                return null;

            var isNegated = line.StartsWith('!');
            if (isNegated)
            {
                line = line[1..];
            }

            if (line.Length == 0)
                return null;

            var directoryOnly = line.EndsWith('/');
            line = line.TrimStart('/').TrimEnd('/');
            if (line.Length == 0)
                return null;

            var normalizedBase = NormalizeDirectoryPath(baseDirectory) ?? baseDirectory;
            return new GitIgnoreRule(normalizedBase, line.Replace('\\', '/'), isNegated, directoryOnly);
        }

        public bool IsMatch(string filePath)
        {
            if (!filePath.StartsWith(BaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relative = Path.GetRelativePath(BaseDirectory, filePath).Replace('\\', '/');
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            if (BasenameOnly)
            {
                var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return segments.Any(segment => Regex.IsMatch(segment));
            }

            return Regex.IsMatch(relative);
        }

        private static Regex BuildRegex(string pattern, bool directoryOnly, bool basenameOnly)
        {
            var escaped = Regex.Escape(pattern)
                .Replace(@"\*\*", "__DOUBLE_STAR__")
                .Replace(@"\*", "[^/]*")
                .Replace(@"\?", "[^/]");

            escaped = escaped.Replace("__DOUBLE_STAR__", ".*");

            string finalPattern;
            if (basenameOnly)
            {
                finalPattern = "^" + escaped + (directoryOnly ? "$" : "$");
            }
            else
            {
                finalPattern = "^" + escaped + (directoryOnly ? "($|/.*)" : "$");
            }

            return new Regex(finalPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
