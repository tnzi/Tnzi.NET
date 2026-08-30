using Microsoft.AspNetCore.Builder;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Tools.Sql;

/// <summary>
/// 端到端验收：加载 <c>Tnzi.AI</c>、什么都不额外注册的应用，必须能在 .NET 默认的
/// <c>ValidateOnBuild</c> / <c>ValidateScopes</c> 下把宿主建起来。
/// </summary>
/// <remarks>
/// <para>
/// 这是消费方实际撞上的那个失败：<c>IReadOnlySqlExecutor</c> / <c>ISchemaInspector</c> 无条件注册，
/// 构造函数却要一个只能由应用提供的 <c>Func&lt;string?, DbConnection&gt;</c>，于是「加载了 AI 但不用
/// SQL 工具」的应用一律启动失败。
/// </para>
/// <para>
/// ⚠️ <b>本文件刻意不写 <c>UseDefaultServiceProvider</c>。</b><c>WebApplication.CreateBuilder</c> 在
/// Development 下把两个开关都置为 true，这里要的就是那个默认值——一旦覆盖，这条测试就退化成
/// 「关掉校验后没报错」，而那正是本轮要根除的写法。仓库里既有的 AI 启动测试
/// （<c>McpServerHttpEndToEndTests</c> 等）都关了校验，因此从来没有覆盖到这条路径。
/// </para>
/// </remarks>
public class AiModuleDefaultValidationBootTests
{
    [Fact]
    public async Task ApplicationLoadingAiModule_BuildsHostUnderDefaultValidation()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // 必须给一个真实 DbContext：仓储注册发生在 AddTnziDbContext 内部，缺了它
            // IRepository<,> 不进服务图，几十个 AI 服务会因「解析不到仓储」而失败——那是夹具
            // 缺配置，不是本测试要验的东西，却足以把真正的信号淹掉。
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:Provider"] = "SQLite",
            ["Database:DbContexts:0:ConnectionString"] = "Data Source=:memory:",
            ["Database:DbContexts:0:DbContextType"] = typeof(AIAnalyticsDbContext).AssemblyQualifiedName,
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AI:DefaultProvider"] = "Test",
            ["AI:Providers:Test:Enabled"] = "true",
            ["AI:Providers:Test:ApiKey"] = "test-key",
            ["AI:Providers:Test:DefaultModel"] = "test-model",
        });

        // 校验开着的情况下能建成宿主，就是「消费方不必为 SQL 工具套件注册任何东西」。
        var app = await TnziApp.CreateAsync<SqlLessAiStartupModule>(builder);

        await using (app)
        {
            Assert.NotNull(app.Services);
        }
    }
}

/// <summary>只依赖 AIModule、不注册任何连接工厂的应用——即验收条件里那个「什么都不做」的消费方。</summary>
[DependsOn(typeof(AIModule))]
internal sealed class SqlLessAiStartupModule : TnziApplicationModule
{
}
