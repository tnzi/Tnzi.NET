using Tnzi.Security;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tnzi.AI.Agents.Definitions;

/// <summary>
/// YAML Agent 定义提供器 — 从文件系统读取 YAML 定义，支持文件监视热重载
/// </summary>
public class YamlAgentDefinitionProvider : IAgentDefinitionProvider, IDisposable
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<YamlAgentDefinitionProvider> _logger;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ConcurrentDictionary<string, (AgentDefinitionDto Definition, string Hash)> _cache = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public YamlAgentDefinitionProvider(IOptionsMonitor<AIOptions> options, ILogger<YamlAgentDefinitionProvider> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        InitializeWatcher();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDefinitionDto>> LoadDefinitionsAsync(CancellationToken ct = default)
    {
        var config = _options.CurrentValue.AgentDefinitions;
        if (!config.Enabled)
            return [];

        var directory = GetAbsoluteDirectory(config.DirectoryPath);
        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("Agent definition directory does not exist: {Directory}", directory);
            return [];
        }

        var yamlFiles = Directory.GetFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(directory, "*.yml", SearchOption.TopDirectoryOnly));

        var definitions = new List<AgentDefinitionDto>();
        foreach (var file in yamlFiles)
        {
            ct.ThrowIfCancellationRequested();

            var definition = await LoadFileAsync(file, ct);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        _logger.LogInformation("Loaded {Count} agent definitions from {Directory}", definitions.Count, directory);
        return definitions;
    }

    /// <inheritdoc />
    public async Task<AgentDefinitionDto?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(name);

        // 先从缓存查找
        var cached = _cache.Values.FirstOrDefault(v =>
            string.Equals(v.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
        if (cached.Definition != null)
            return cached.Definition;

        // 缓存未命中，重新加载
        var definitions = await LoadDefinitionsAsync(ct);
        return definitions.FirstOrDefault(d =>
            string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<AgentDefinitionDto?> LoadFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var hash = ComputeHash(content);

            // 缓存命中检查
            if (_cache.TryGetValue(filePath, out var cached) && cached.Hash == hash)
                return cached.Definition;

            var definition = _yamlDeserializer.Deserialize<AgentDefinitionDto>(content);
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
            {
                _logger.LogWarning("Invalid agent definition in file {File}: missing 'name' field", filePath);
                return null;
            }

            definition.DefinitionHash = hash;
            _cache[filePath] = (definition, hash);
            _logger.LogDebug("Loaded agent definition '{Name}' from {File}", definition.Name, filePath);
            return definition;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse agent definition file: {File}", filePath);
            return null;
        }
    }

    private void InitializeWatcher()
    {
        var config = _options.CurrentValue.AgentDefinitions;
        if (!config.Enabled || !config.WatchForChanges)
            return;

        var directory = GetAbsoluteDirectory(config.DirectoryPath);
        if (!Directory.Exists(directory))
            return;

        try
        {
            _watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;

            _logger.LogDebug("Started watching agent definition directory: {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize file watcher for agent definitions");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsYamlFile(e.FullPath)) return;

        // 清除缓存以触发下次访问时重新加载
        _cache.TryRemove(e.FullPath, out _);
        _logger.LogDebug("Agent definition file changed, cache invalidated: {File}", e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (!IsYamlFile(e.FullPath)) return;

        _cache.TryRemove(e.FullPath, out _);
        _logger.LogDebug("Agent definition file deleted, cache removed: {File}", e.FullPath);
    }

    private static bool IsYamlFile(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAbsoluteDirectory(string directoryPath)
    {
        return Path.IsPathRooted(directoryPath)
            ? directoryPath
            : Path.Combine(AppContext.BaseDirectory, directoryPath);
    }

    private static string ComputeHash(string content)
    {
        return HashHelper.GetSha256(content);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Deleted -= OnFileDeleted;
            _watcher.Dispose();
        }
    }
}
