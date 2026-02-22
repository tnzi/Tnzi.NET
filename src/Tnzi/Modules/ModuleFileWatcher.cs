
namespace Tnzi.Modules;

/// <summary>
/// 模块文件监听器
/// 监听模块程序集文件的变化
/// </summary>
[ExperimentalApi(Reason = "Hot reload is not fully implemented")]
public class ModuleFileWatcher : IDisposable
{
    private readonly ILogger<ModuleFileWatcher> _logger;
    private readonly ModuleHotReloadOptions _options;
    private FileSystemWatcher? _watcher;
    private readonly object _lockObject = new();
    private bool _disposed = false;
    private Timer? _reloadTimer;
    private readonly HashSet<string> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 是否正在监听文件变化
    /// </summary>
    public bool IsWatching => _watcher != null && _watcher.EnableRaisingEvents;

    /// <summary>
    /// 文件变化事件
    /// </summary>
    public event EventHandler<FileSystemEventArgs>? FileChanged;

    /// <summary>
    /// 初始化一个<see cref="ModuleFileWatcher"/>类型的新实例
    /// </summary>
    public ModuleFileWatcher(
        ILogger<ModuleFileWatcher> logger,
        IOptions<ModuleHotReloadOptions> options)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value;
    }

    /// <summary>
    /// 开始监听
    /// </summary>
    public void StartWatching()
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Module hot reload is disabled");
            return;
        }

        lock (_lockObject)
        {
            if (_watcher != null)
            {
                _logger.LogWarning("File watcher is already started");
                return;
            }

            var watchPath = _options.WatchDirectory ?? AppContext.BaseDirectory;
            if (!Directory.Exists(watchPath))
            {
                _logger.LogWarning("Watch directory does not exist: {WatchPath}", watchPath);
                return;
            }

            _watcher = new FileSystemWatcher(watchPath)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = _options.IncludeSubdirectories,
                EnableRaisingEvents = true
            };

            // 设置过滤模式
            foreach (var pattern in _options.WatchedFileExtensions)
            {
                _watcher.Filters.Add(pattern);
            }

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;

            // 创建延迟重载定时器
            _reloadTimer = new Timer(OnReloadTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            _logger.LogInformation("Started watching module files in: {WatchPath}", watchPath);
        }
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public void StopWatching()
    {
        lock (_lockObject)
        {
            if (_watcher == null)
                return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Deleted -= OnFileChanged;
            _watcher.Dispose();
            _watcher = null;

            _reloadTimer?.Dispose();
            _reloadTimer = null;

            _pendingFiles.Clear();

            _logger.LogInformation("Stopped watching module files");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // 检查是否在排除的目录中
        if (IsExcluded(e.FullPath))
            return;

        // 检查文件扩展名
        if (!IsWatchedFile(e.FullPath))
            return;

        lock (_lockObject)
        {
            _pendingFiles.Add(e.FullPath);
        }

        // 重置定时器（延迟重载）
        _reloadTimer?.Change(_options.ReloadDelayMs, Timeout.Infinite);

        _logger.LogDebug("File changed: {FilePath}", e.FullPath);
    }

    private void OnReloadTimerElapsed(object? state)
    {
        lock (_lockObject)
        {
            if (_pendingFiles.Count == 0)
                return;

            var files = _pendingFiles.ToList();
            _pendingFiles.Clear();

            foreach (var filePath in files)
            {
                try
                {
                    var args = new FileSystemEventArgs(
                        WatcherChangeTypes.Changed,
                        Path.GetDirectoryName(filePath)!,
                        Path.GetFileName(filePath));

                    FileChanged?.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error raising file changed event for: {FilePath}", filePath);
                }
            }
        }
    }

    private bool IsExcluded(string filePath)
    {
        var relativePath = Path.GetRelativePath(_options.WatchDirectory ?? AppContext.BaseDirectory, filePath);
        var directory = Path.GetDirectoryName(relativePath);

        if (string.IsNullOrEmpty(directory))
            return false;

        var segments = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (_options.ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool IsWatchedFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var pattern in _options.WatchedFileExtensions)
        {
            var searchPattern = pattern.TrimStart('*');
            if (fileName.EndsWith(searchPattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        StopWatching();
        _disposed = true;
    }
}