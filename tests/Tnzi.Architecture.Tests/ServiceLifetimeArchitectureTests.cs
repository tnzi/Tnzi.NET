using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using Tnzi.Modules;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 全模块图的服务生命周期门禁。
/// </summary>
public partial class ServiceLifetimeArchitectureTests
{
    /// <summary>
    /// 任何一个框架模块都不得让消费方为了启动而关掉作用域校验。
    /// </summary>
    /// <remarks>
    /// <para>
    /// captive dependency（Singleton 持有 Scoped）在 .NET 默认配置下只在 Development 暴露，
    /// 而消费方遇到它时最省事的解法是在 <c>Program.cs</c> 写一句
    /// <c>UseDefaultServiceProvider(o =&gt; o.ValidateScopes = false)</c>。这句话是<b>宿主级</b>的：
    /// 它不只压掉框架这一处，还连带压掉消费方自己代码里的每一处同类错误。框架欠消费方的是
    /// 「不要逼他们做这个交易」，所以这道门禁建在框架侧。Tnzi.Hangfire 就实际发生过一次
    /// （<c>HangfireBackgroundJobManager</c> 注入 <see cref="Tnzi.MultiTenancy.ICurrentTenant"/>）。
    /// </para>
    /// <para>
    /// <b>为什么只筛 "Cannot consume scoped service"：</b>生命周期违规有专属措辞，据此切分。
    /// 「某某服务解析不到」是另一类问题，由
    /// <see cref="NoModule_RequiresConsumersToRegisterServicesTheyDoNotUse"/> 单独把关——
    /// ⚠️ 这两类<b>都</b>会让消费方在默认 <c>ValidateOnBuild</c> 下启动失败，因而<b>都</b>会诱使人
    /// 关掉宿主级校验。本测试早期版本把非 captive 的那一堆整体注释为「夹具局限」，
    /// AI SQL 工具套件的真实缺陷就藏在那堆里被放过了。
    /// </para>
    /// <para>
    /// <b>已知的覆盖上界（不要据此声称「全图无 captive」）：</b><c>ValidateOnBuild</c> 对每个描述符
    /// 只报第一个问题，所以一个先因缺注册而失败的描述符，可能盖住它下游的生命周期违规。
    /// 本门禁保证的是「凡是能被校验到的都没有违规」，属于下界；随着夹具越完整，覆盖面越大。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoModule_ForcesConsumersToDisableScopeValidation()
    {
        var (modules, messages) = await ValidateAllModulesAsync();

        var captiveDependencies = messages
            .Where(m => m.Contains("Cannot consume scoped service", StringComparison.Ordinal))
            .ToList();

        Assert.True(captiveDependencies.Count == 0,
            $"{modules.Count} 个模块的服务图里存在 captive dependency，"
            + "加载它们的应用将被迫关闭 ValidateScopes：\n"
            + string.Join("\n", captiveDependencies));
    }

    /// <summary>
    /// 夹具本身解析不出来、且<b>已知无害</b>的依赖类型。清单之外的任何一个都算缺陷。
    /// </summary>
    /// <remarks>
    /// 这三类都由「真实宿主有、合成夹具没有」造成，与消费方无关：
    /// <list type="bullet">
    /// <item><c>UserManager/RoleManager/IPasskeyHandler</c> —— ASP.NET Core Identity 的运行时对象，
    /// 需要一个真正的 Identity DbContext 实例，本夹具的 DbContext 不是。</item>
    /// <item><c>IRepository&lt;RAG 实体&gt;</c> —— 那些实体属于夹具未注册的 DbContext。</item>
    /// <item><c>ITnziApplication</c> —— 由 <c>TnziApp</c> 在真实启动流程中创建，裸 builder 里没有。</item>
    /// </list>
    /// </remarks>
    private static readonly string[] KnownFixtureGaps =
    [
        "Microsoft.AspNetCore.Identity.IPasskeyHandler`1[Tnzi.Identity.Entities.User]",
        "Microsoft.AspNetCore.Identity.RoleManager`1[Tnzi.Identity.Entities.Role]",
        "Microsoft.AspNetCore.Identity.UserManager`1[Tnzi.Identity.Entities.User]",
        "Tnzi.Domain.Repositories.IRepository`2[Tnzi.AI.Rag.Entities.DocumentChunk,System.Guid]",
        "Tnzi.Domain.Repositories.IRepository`2[Tnzi.AI.Rag.Entities.KnowledgeBase,System.Guid]",
        "Tnzi.Domain.Repositories.IRepository`2[Tnzi.AI.Rag.Entities.KnowledgeDocument,System.Guid]",
        "Tnzi.Domain.Repositories.IRepository`2[Tnzi.AI.Rag.Entities.KnowledgeGraphNode,System.Guid]",
        "Tnzi.ITnziApplication",
    ];

    /// <summary>
    /// 框架不得要求消费方为「自己根本不用的功能」注册东西才能启动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这道门禁与 <see cref="NoModule_ForcesConsumersToDisableScopeValidation"/> 是一对：
    /// 前者管「生命周期错配」，本条管「缺注册」。<b>两者对消费方的后果完全相同</b>——默认
    /// <c>ValidateOnBuild</c> 下启动失败，而最省事的出路都是关掉宿主级校验。
    /// </para>
    /// <para>
    /// <b>实发案例：</b><c>AIModule</c> 曾无条件注册 <c>IReadOnlySqlExecutor</c> /
    /// <c>ISchemaInspector</c>，二者的构造函数要一个只能由应用提供的
    /// <c>Func&lt;string?, DbConnection&gt;</c>。于是「加载了 Tnzi.AI 但不想让助手跑 SQL」的应用
    /// 一律启动失败。可选能力的注册不该是强制的——代价的方向反了。
    /// </para>
    /// <para>
    /// <b>断言用集合相等而不是「不新增」：</b>清单变短（夹具变完整）同样要求改这里，
    /// 迫使每一次增减都被看见一次。宁可偶尔为好事变红，也不要让下一个真缺陷混进
    /// 「已知无害」里——上一版正是把它们整体当噪音，才放过了 SQL 那两条。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoModule_RequiresConsumersToRegisterServicesTheyDoNotUse()
    {
        var (modules, messages) = await ValidateAllModulesAsync();

        var missingTypes = messages
            .Select(m => MissingServiceTypeRegex().Match(m))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var unexpected = missingTypes.Except(KnownFixtureGaps, StringComparer.Ordinal).ToList();
        var goneStale = KnownFixtureGaps.Except(missingTypes, StringComparer.Ordinal).ToList();

        Assert.True(unexpected.Count == 0,
            $"{modules.Count} 个模块的服务图里，有服务依赖了框架自己不注册、只能由应用提供的类型。"
            + "加载了相关模块却不使用该功能的应用会在默认 ValidateOnBuild 下启动失败：\n"
            + string.Join("\n", unexpected)
            + "\n\n修法是给它一个可解析的回退（调用时才报可执行的错误），而不是要求所有人都注册。"
            + "若确认某项确属夹具局限，补进 KnownFixtureGaps 并写明理由。");

        Assert.True(goneStale.Count == 0,
            "KnownFixtureGaps 里这些条目已经不再出现，说明夹具变完整了。"
            + "请把它们从清单里删掉，让清单继续如实反映覆盖面：\n" + string.Join("\n", goneStale));
    }

    [GeneratedRegex(@"Unable to resolve service for type '([^']+)'")]
    private static partial Regex MissingServiceTypeRegex();

    /// <summary>
    /// 把全模块图装进一个真实宿主并跑一次容器校验，返回模块列表与校验错误消息。
    /// </summary>
    private static async Task<(IReadOnlyList<IModuleDescriptor> Modules, IReadOnlyList<string> Messages)>
        ValidateAllModulesAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:Provider"] = "SQLite",
            ["Database:DbContexts:0:ConnectionString"] = "Data Source=:memory:",
            ["Database:DbContexts:0:DbContextType"] = typeof(ArchitectureTestDbContext).AssemblyQualifiedName,
            ["Identity:Jwt:SecretKey"] = "test-key-for-architecture-tests-only-minimum-32-chars",
            ["Hangfire:Enabled"] = "true",
            ["Hangfire:StorageType"] = "Memory",
        });

        var modules = new ModuleLoader().LoadModules(builder.Services, typeof(AllModulesStartupModule));
        var context = new ServiceConfigurationContext(builder.Services, builder.Configuration);

        // 配置阶段本身不能崩：崩掉的模块不会往容器里注册任何东西，等于悄悄退出被审计的范围
        // （这正是本项目历史上「门禁假绿」的成因，见 ModuleTestHelper 的注释）。
        var phaseFailures = new List<string>();
        foreach (var module in modules)
        {
            await RunPhaseAsync(module, "PreConfigureServices", m => m.Instance.PreConfigureServicesAsync(context), phaseFailures);
            await RunPhaseAsync(module, "ConfigureServices", m => m.Instance.ConfigureServicesAsync(context), phaseFailures);
        }

        foreach (var module in modules)
        {
            await RunPhaseAsync(module, "PostConfigureServices", m => m.Instance.PostConfigureServicesAsync(context), phaseFailures);
        }

        Assert.True(phaseFailures.Count == 0,
            "模块配置阶段抛异常，本门禁的覆盖面已经不完整：\n" + string.Join("\n", phaseFailures));

        var exception = Record.Exception(() => builder.Build());
        var messages = (exception as AggregateException)?.InnerExceptions
            .Select(e => e.Message)
            .ToList() ?? [];

        return (modules, messages);
    }

    private static async Task RunPhaseAsync(
        IModuleDescriptor module,
        string phase,
        Func<IModuleDescriptor, Task> action,
        List<string> failures)
    {
        try
        {
            await action(module);
        }
        catch (Exception ex)
        {
            failures.Add($"{module.Type.Name}.{phase}: {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}");
        }
    }
}
