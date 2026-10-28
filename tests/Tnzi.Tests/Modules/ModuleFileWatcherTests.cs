namespace Tnzi.Tests.Modules;

/// <summary>
/// 相对的 <see cref="ModuleHotReloadOptions.WatchDirectory"/> 按应用程序根目录（<see cref="AppContext.BaseDirectory"/>）解析，
/// 与它的默认值同一个基准。此前原样交给 <see cref="FileSystemWatcher"/>，按进程工作目录解析：
/// Windows 服务（工作目录是 system32）或从别的目录启动的进程只记一条 Warning、从不监听。
/// </summary>
public class ModuleFileWatcherTests : IDisposable
{
    private readonly string _relativeName = "hotreload-watch-" + Guid.NewGuid().ToString("N");
    private readonly string _absolutePath;

    public ModuleFileWatcherTests()
    {
        _absolutePath = Path.Combine(AppContext.BaseDirectory, _relativeName);
        Directory.CreateDirectory(_absolutePath);
    }

    private sealed class RecordingLogger : ILogger<ModuleFileWatcher>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public void ARelativeWatchDirectory_IsResolvedAgainstTheApplicationRoot()
    {
        var logger = new RecordingLogger();
        using var watcher = new ModuleFileWatcher(logger, Microsoft.Extensions.Options.Options.Create(
            new ModuleHotReloadOptions { Enabled = true, WatchDirectory = _relativeName }));

        watcher.StartWatching();

        Assert.True(watcher.IsWatching);
        Assert.Contains(logger.Messages, m => m.Contains(_absolutePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnAbsoluteWatchDirectory_IsUsedAsIs()
    {
        var logger = new RecordingLogger();
        using var watcher = new ModuleFileWatcher(logger, Microsoft.Extensions.Options.Options.Create(
            new ModuleHotReloadOptions { Enabled = true, WatchDirectory = _absolutePath }));

        watcher.StartWatching();

        Assert.True(watcher.IsWatching);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_absolutePath, recursive: true);
        }
        catch (IOException)
        {
            // 监听句柄释放稍有延迟时目录可能暂时删不掉；它在测试输出目录里，不影响其它用例
        }
    }
}
