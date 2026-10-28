using Microsoft.AspNetCore.Builder;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// 启动失败时 <c>TnziApp.RunAsync</c> 把含完整堆栈的报告写进 <c>startup-error.log</c>，
/// 部署方用 <c>Tnzi:WriteStartupErrorLog=false</c>（或代码回调）关掉落盘，控制台输出保留。
/// </summary>
/// <remarks>
/// 同一个文件在任何一次启动成功时会被删除，别的测试类并行建应用会抹掉这里刚写下的文件，
/// 所以这组测试不与其它测试并行。
/// </remarks>
[Collection(nameof(StartupErrorLogCollection))]
public class TnziAppStartupErrorLogTests
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");

    [Fact]
    public async Task RunAsync_WritesTheStartupErrorLog_ByDefault()
    {
        DeleteLog();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TnziApp.RunAsync<TestHostingLiteStartupModule>([], configureBuilder: FailStartup));

            Assert.True(File.Exists(LogPath));
            Assert.Contains("startup failed on purpose", await File.ReadAllTextAsync(LogPath));
        }
        finally
        {
            DeleteLog();
        }
    }

    [Fact]
    public async Task RunAsync_SkipsTheStartupErrorLog_WhenTheConfigurationSectionTurnsItOff()
    {
        DeleteLog();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TnziApp.RunAsync<TestHostingLiteStartupModule>(
                    ["--Tnzi:WriteStartupErrorLog=false"], configureBuilder: FailStartup));

            Assert.False(File.Exists(LogPath));
        }
        finally
        {
            DeleteLog();
        }
    }

    [Fact]
    public async Task RunAsync_SkipsTheStartupErrorLog_WhenTheCodeCallbackTurnsItOff()
    {
        DeleteLog();
        try
        {
            // 代码回调优先于配置节，与容器里 IOptions<TnziOptions> 的优先级一致
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TnziApp.RunAsync<TestHostingLiteStartupModule>(
                    ["--Tnzi:WriteStartupErrorLog=true"],
                    configureOptions: options => options.WriteStartupErrorLog = false,
                    configureBuilder: FailStartup));

            Assert.False(File.Exists(LogPath));
        }
        finally
        {
            DeleteLog();
        }
    }

    private static void FailStartup(WebApplicationBuilder builder)
        => throw new InvalidOperationException("startup failed on purpose");

    private static void DeleteLog()
    {
        if (File.Exists(LogPath))
        {
            File.Delete(LogPath);
        }
    }
}

[CollectionDefinition(nameof(StartupErrorLogCollection), DisableParallelization = true)]
public class StartupErrorLogCollection
{
}
