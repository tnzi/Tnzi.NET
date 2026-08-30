using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Tnzi.Modules;
using Tnzi.MultiTenancy;

namespace Tnzi.Hangfire.Tests;

/// <summary>
/// 「加载本模块要让宿主付出什么代价」的契约测试。
/// </summary>
/// <remarks>
/// 这些断言的对象不是模块内部行为，而是<b>消费方被迫做出的让步</b>：关掉作用域校验、
/// 在自己的 csproj 里钉一个本不该由它管的包版本。两者都发生过，且都不会让任何既有测试变红。
/// </remarks>
public class HangfireHostContractTests
{
    /// <summary>
    /// 加载 HangfireModule 的应用必须能在 .NET 默认的作用域校验下正常启动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 回归背景：<c>HangfireBackgroundJobManager</c> 曾以 Singleton 身份直接注入 Scoped 的
    /// <see cref="ICurrentTenant"/>（captive dependency），消费方只能在 <c>Program.cs</c> 里写
    /// <c>UseDefaultServiceProvider(o =&gt; o.ValidateScopes = false)</c> 才启动得起来。
    /// 该写法是<b>宿主级</b>的：它连带关掉了消费方自己代码里的每一处 captive dependency 检查。
    /// 一个框架模块不该让应用付出这个代价。
    /// </para>
    /// <para>
    /// 既有的 <c>HangfireBackgroundJobManager_AsSingleton_PassesScopeValidation</c> 只注册两个服务，
    /// 覆盖的是「这个类自己干净」；本测试建的是<b>真实宿主</b>，覆盖的是「本模块注册的全部描述符
    /// （含 <c>AddHangfire</c> / <c>AddHangfireServer</c> 带进来的那批）在真实容器里一起校验也干净」——
    /// 消费方遇到的正是后者。<c>WebApplication.CreateBuilder</c> 在 Development 下把
    /// <c>ValidateScopes</c> 与 <c>ValidateOnBuild</c> 都置为 true，此处刻意<b>不覆盖</b>它们。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Memory")]
    [InlineData("PostgreSQL")]
    public async Task LoadingModule_DoesNotForceHostToDisableScopeValidation(string storageType)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hangfire:Enabled"] = "true",
            ["Hangfire:StorageType"] = storageType,
            ["Hangfire:ConnectionString"] = "Host=127.0.0.1;Database=unused;Username=u;Password=p",
            ["Hangfire:Dashboard:Enabled"] = "false",
        });

        // ICurrentTenant 平时由 EFCoreModule 注册为 Scoped，正是被捕获的那一个。
        builder.Services.AddScoped<ICurrentTenant, CurrentTenant>();

        var module = new HangfireModule();
        var context = new ServiceConfigurationContext(builder.Services, builder.Configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        // 校验开着的情况下 Build() 不抛，就是「消费方不必关校验」。
        var exception = Record.Exception(() => builder.Build());

        Assert.True(exception == null,
            $"加载 HangfireModule（{storageType} 存储）后宿主在默认作用域校验下启动失败，"
            + $"消费方将被迫关闭 ValidateScopes：\n{exception}");
    }

    /// <summary>
    /// 本模块拉进闭包的 Npgsql 必须与 EF Provider 的处于同一个主版本线。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hangfire.PostgreSql 1.21.1（当前最新版）声明的依赖是 <c>Npgsql &gt;= 6.0.11</c>，
    /// 比 .NET 10 上 <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> 带来的 Npgsql 差四个主版本
    /// （框架自己的 <c>Tnzi.AI.Rag</c> 就在用后者）。放任不管，同一个闭包里会同时存在两个主版本的
    /// 同名程序集：只引用 Tnzi.Hangfire 而没引用 EF Provider 的项目按 6.0.11 编译，宿主运行时却加载
    /// 10.x，中间每个项目刷一片 MSB3277 警告，把真正的警告淹掉。消费方为此在自己的公共层钉了一个
    /// Npgsql 版本——那本不是它该管的事。
    /// </para>
    /// <para>
    /// 断言只卡主版本下界，不卡具体版本：NuGet 的依赖版本是<b>下界不是锁</b>，
    /// 消费方在更高的 Npgsql 上应当照常向上归一。
    /// </para>
    /// </remarks>
    [Fact]
    public void NpgsqlInClosure_IsMajorAlignedWithEfProvider()
    {
        var npgsql = typeof(NpgsqlConnection).Assembly.GetName();

        Assert.True(npgsql.Version?.Major >= 10,
            $"Tnzi.Hangfire 闭包里的 Npgsql 是 {npgsql.Version}，落后于 EF Provider 的 10.x。"
            + "消费方会重新出现 MSB3277 Npgsql 版本冲突，并被迫自己钉 Npgsql 版本。");
    }

    /// <summary>
    /// Hangfire.PostgreSql 在这个更新了四个主版本的 Npgsql 上必须真的能跑。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 抬版本的前提是「1.21.1 扛得住 Npgsql 10」，这不能靠假设。构造 <c>PostgreSqlStorage</c> 会把
    /// Hangfire.PostgreSql 引用到的 Npgsql 类型全部加载并绑定一遍：如果 Npgsql 6→10 之间删改过它用到的
    /// API，这里抛的是 <c>TypeLoadException</c> / <c>MissingMethodException</c> / <c>FileNotFoundException</c>，
    /// 而不是安静通过。
    /// </para>
    /// <para>
    /// <c>PrepareSchemaIfNecessary = false</c> 是为了<b>不发起真实连接</b>：留着默认值会让每次跑测试
    /// 都去连一个不存在的库，靠超时结束（实测单这一条就吃掉 40 秒），而建表本身并不是这里要验的东西。
    /// </para>
    /// </remarks>
    [Fact]
    public void PostgreSqlStorage_BindsAgainstCurrentNpgsql()
    {
        var options = new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = false };
        var storage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory("Host=127.0.0.1;Database=unused;Username=u;Password=p", options),
            options);

        Assert.NotNull(storage.GetMonitoringApi());
    }
}
