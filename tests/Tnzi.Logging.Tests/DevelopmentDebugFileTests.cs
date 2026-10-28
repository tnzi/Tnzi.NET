using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Tnzi.Logging.Tests.RequestLogging;
using Tnzi.Modules;

namespace Tnzi.Logging.Tests;

/// <summary>
/// 开发环境此前会悄悄打开 Debug 文件 sink，而全局最低级别仍是 Information：Logs/Debug 里一条 Debug 事件都没有，
/// 只是 Information 目录的副本；管理端日志页读的 IOptions 里它又是关着的，两边说法不一。
/// 现在生效配置就是配置文件里写的那一份，不随环境改写。
/// </summary>
/// <remarks>
/// 改的是进程级环境变量与 <see cref="Log.Logger"/>，故进 <see cref="SerilogGlobalCollection"/>，
/// 与同样建宿主、读环境的测试类串行。
/// </remarks>
[Collection(SerilogGlobalCollection.Name)]
public sealed class DevelopmentDebugFileTests : IDisposable
{
    private readonly Serilog.ILogger _previousLogger = Log.Logger;
    private readonly string? _previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tnzi-log-dev", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Log.CloseAndFlush();
        Log.Logger = _previousLogger;
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _previousEnvironment);
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不该让测试失败
        }
    }

    [Fact]
    public async Task InDevelopment_TheDebugFileSinkFollowsConfiguration()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:EnableConsole"] = "false",
            ["Logging:EnableFile"] = "true",
            ["Logging:BasePath"] = _root,
        }).Build();
        var context = new ServiceConfigurationContext(new ServiceCollection(), configuration, "Development");
        var module = new LoggingModule();

        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        Directory.Exists(Path.Combine(_root, "Information")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_root, "Debug")).ShouldBeFalse();
    }
}
