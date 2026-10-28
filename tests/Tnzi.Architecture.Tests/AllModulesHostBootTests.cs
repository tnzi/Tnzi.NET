using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tnzi.Data;
using Tnzi.Hosting;
using Tnzi.Identity.Entities;
using Tnzi.Identity.Services;
using Tnzi.Modules;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 由<b>全部</b>模块组成的真实 ASP.NET Core 宿主：启动、托管服务、请求、停机走一遍。
/// </summary>
/// <remarks>
/// <para>
/// 其它架构门禁只建模块图（<see cref="ArchitectureModuleGraph"/> 跑到 <c>ConfigureServices</c> 为止）：
/// 不构建宿主、不跑 <c>OnApplicationInitialization</c>、不启动托管服务、不发请求。构造期依赖环、
/// 生命周期错配（单例吃作用域服务）、托管服务在 <c>StartAsync</c> 里抛、中间件管线装配错这类问题只在真实启动时暴露，
/// 而一次 DI 或启动注册的改动（前转工厂、托管服务、具名 HttpClient、中间件挪位）正是它们的来源。
/// </para>
/// <para>
/// 外部依赖一律不连，所以这条测试在没有任何外部服务的机器上也能跑：数据库是一个临时 SQLite 文件
/// （没有迁移，启动时的初始化器按模型建表）；Redis 指向一个没人监听的端口且不中止连接，
/// 依赖它的写入按各自的失败语义报错记日志（例如权限目录同步跳过这一次），不影响启动；RabbitMQ / Kafka 在后台重连。
/// 宿主用 <c>TestServer</c>，不占端口。
/// 一次启动要装 50 多个模块、建全部实体的表，所以整条链路放在一个测试里，而不是每个断言各启动一次。
/// </para>
/// <para>
/// 建表要先建模型，所以同一次启动也守着 <b>EF 模型校验零警告</b>：每个消费方启动时都会打出框架实体配置的警告
/// （转换后的集合没有值比较器 = 就地修改不落库；建在 <c>TenantId</c> 上的索引在单租户下被连带丢掉 = 约束不存在），
/// 而消费方无从修它们，只能学会无视，真正的警告随之淹没。
/// </para>
/// </remarks>
public sealed class AllModulesHostBootTests
{
    /// <summary>停机给的上限：托管服务的 StopAsync 都应在这之内结束。</summary>
    private static readonly TimeSpan ShutdownBound = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task AllModulesHost_Starts_ServesAnonymousAndAuthenticatedEndpoints_AndStops()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "tnzi-boot-" + Guid.NewGuid().ToString("N") + ".db");
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ApplicationName = typeof(AllModulesHostBootTests).Assembly.GetName().Name,
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbContexts:0:Name"] = "Default",
                ["Database:DbContexts:0:DbContextType"] = typeof(AllModulesBootDbContext).AssemblyQualifiedName,
                ["Database:DbContexts:0:ConnectionString"] = $"Data Source={databasePath}",
                ["Database:DbContexts:0:Provider"] = "Sqlite",
                ["AspNetCore:EnableForwardedHeaders"] = "false",
                // 加载了 Redis 模块就必须给连接串；指向一个没人监听的端口、不中止连接：启动不依赖 Redis 在场
                ["Redis:ConnectionString"] = "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200",
            });

            var modelWarnings = new ModelWarningCapture();
            builder.Logging.AddProvider(modelWarnings);

            app = await TnziApp.CreateAsync<AllModulesBootStartupModule>(builder);

            // 依次启动每一个托管服务：任何一个在 StartAsync 里抛，这里就抛
            await app.StartAsync();

            // 后台服务的第一轮循环不能把自己弄死
            await Task.Delay(TimeSpan.FromSeconds(1));
            foreach (var service in app.Services.GetServices<IHostedService>().OfType<BackgroundService>())
            {
                (service.ExecuteTask?.IsFaulted ?? false).ShouldBeFalse(
                    $"{service.GetType().FullName} faulted: {service.ExecuteTask?.Exception?.GetBaseException()}");
            }

            // 启动时的初始化器已按模型建表，模型校验此时已跑过
            modelWarnings.Warnings.ShouldBeEmpty(
                "EF model validation warned about framework entity configuration:" + Environment.NewLine +
                string.Join(Environment.NewLine, modelWarnings.Warnings));

            var client = app.GetTestClient();

            var anonymous = await client.GetAsync("/api/auth/config");
            anonymous.StatusCode.ShouldBe(HttpStatusCode.OK, await anonymous.Content.ReadAsStringAsync());

            var withoutToken = await client.GetAsync("/api/users/profile");
            withoutToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await withoutToken.Content.ReadAsStringAsync());

            // 带一枚真实签发的访问令牌：认证、会话校验、控制器与数据库一路走通
            var token = await IssueAccessTokenAsync(app.Services);
            using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/users/profile");
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var withToken = await client.SendAsync(profileRequest);
            withToken.StatusCode.ShouldBe(HttpStatusCode.OK, await withToken.Content.ReadAsStringAsync());

            using var stopBound = new CancellationTokenSource(ShutdownBound);
            await app.StopAsync(stopBound.Token);
            stopBound.IsCancellationRequested.ShouldBeFalse($"the host did not stop within {ShutdownBound.TotalSeconds} seconds");
        }
        finally
        {
            if (app != null)
            {
                await app.DisposeAsync();
            }

            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // 临时目录里的残留文件不影响结论
                }
            }
        }
    }

    /// <summary>建一个用户并经框架的令牌服务给他签一枚访问令牌（不走登录接口：那条路要过验证码、两步验证等配置）。</summary>
    private static async Task<string> IssueAccessTokenAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = "boot-smoke", Email = "boot-smoke@example.com", EmailConfirmed = true };
        var created = await userManager.CreateAsync(user, "Boot-Smoke-Passw0rd!");
        created.Succeeded.ShouldBeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));

        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateToken(user, []);
    }

    /// <summary>
    /// 收下 EF 模型构建与校验阶段（<c>Microsoft.EntityFrameworkCore.Model*</c> 类别）的警告。
    /// 日志模块把经 <c>builder.Logging</c> 注册的 provider 桥进 Serilog 管线，所以它在本宿主里收得到。
    /// </summary>
    private sealed class ModelWarningCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Warnings { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CategoryLogger(ModelWarningCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel)
                => logLevel >= LogLevel.Warning && category.StartsWith("Microsoft.EntityFrameworkCore.Model", StringComparison.Ordinal);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    owner.Warnings.Enqueue($"{eventId.Name}: {formatter(state, exception)}");
                }
            }
        }
    }

    /// <summary>启动冒烟的模块集合 = 全模块清单 − RAG（见 <see cref="AllModulesBootStartupModule"/>）。</summary>
    [Fact]
    public void BootModule_IsEveryModuleExceptRag()
    {
        static HashSet<Type> DependsOn(Type module)
            => module.GetCustomAttributes(typeof(DependsOnAttribute), inherit: false)
                .Cast<DependsOnAttribute>()
                .SelectMany(a => a.DependedModuleTypes)
                .ToHashSet();

        var expected = DependsOn(typeof(AllModulesStartupModule));
        expected.Remove(typeof(Tnzi.AI.Rag.AIRagModule)).ShouldBeTrue("the full module list no longer contains the RAG module; update the boot module's remark");

        DependsOn(typeof(AllModulesBootStartupModule)).ShouldBe(expected, ignoreOrder: true);
    }
}

/// <summary>
/// 启动冒烟用的启动模块：<see cref="AllModulesStartupModule"/> 的全部模块，减去只能跑在 PostgreSQL + pgvector 上的 <c>Tnzi.AI.Rag</c>。
/// </summary>
/// <remarks>
/// <para>
/// RAG 子模块有自己的 <c>RagDbContext</c>（向量列 <c>vector(n)</c>、pgvector 类型映射），只能配一个 PostgreSQL 连接，
/// 一台没有外部服务的机器起不来它；其余模块全部在场。
/// </para>
/// <para>
/// 列表与 <see cref="AllModulesStartupModule"/> 逐项对应（类型写全限定名，理由同那里），
/// <see cref="AllModulesHostBootTests.BootModule_IsEveryModuleExceptRag"/> 守着两份不走样：
/// 新模块加进那边而没加进这里，那条测试就红。
/// </para>
/// </remarks>
[DependsOn(
    typeof(Tnzi.AspNetCore.AspNetCoreModule),
    typeof(Tnzi.EFCore.EFCoreModule),
    typeof(Tnzi.Mapster.MapsterModule),
    typeof(Tnzi.Logging.LoggingModule),
    typeof(Tnzi.Localization.LocalizationModule),
    typeof(Tnzi.Swagger.SwaggerModule),
    typeof(Tnzi.SignalR.SignalRModule),
    typeof(Tnzi.HealthChecks.HealthChecksModule),
    typeof(Tnzi.Redis.RedisCachingModule),
    typeof(Tnzi.RabbitMQ.RabbitMQEventBusModule),
    typeof(Tnzi.Kafka.KafkaEventBusModule),
    typeof(Tnzi.OpenTelemetry.OpenTelemetryModule),
    typeof(Tnzi.Performance.PerformanceModule),
    typeof(Tnzi.Hangfire.HangfireModule),
    typeof(Tnzi.Imaging.ImagingModule),
    typeof(Tnzi.Identity.IdentityModule),
    typeof(Tnzi.Identity.Presence.IdentityPresenceModule),
    typeof(Tnzi.Authorization.AuthorizationModule),
    typeof(Tnzi.Storage.StorageModule),
    typeof(Tnzi.Template.TemplateModule),
    typeof(Tnzi.Notification.NotificationModule),
    typeof(Tnzi.Notification.Push.NotificationPushModule),
    typeof(Tnzi.Storage.Cloud.StorageCloudModule),
    typeof(Tnzi.Payment.Stripe.PaymentStripeModule),
    typeof(Tnzi.Payment.PayPal.PaymentPayPalModule),
    typeof(Tnzi.Storage.Workspace.StorageWorkspaceModule),
    typeof(Tnzi.Finance.Offers.FinanceOffersModule),
    typeof(Tnzi.Authorization.DataAuth.AuthorizationDataAuthModule),
    typeof(Tnzi.Identity.Organization.IdentityOrganizationModule),
    typeof(Tnzi.Payment.Billing.PaymentBillingModule),
    typeof(Tnzi.Payment.Subscriptions.PaymentSubscriptionsModule),
    typeof(Tnzi.Payment.Promotions.PaymentPromotionsModule),
    typeof(Tnzi.Audit.AuditModule),
    typeof(Tnzi.System.SystemModule),
    typeof(Tnzi.Feature.FeatureModule),
    typeof(Tnzi.Chat.ChatModule),
    typeof(Tnzi.Payment.PaymentModule),
    typeof(Tnzi.Documents.DocumentsModule),
    typeof(Tnzi.Signing.SigningModule),
    typeof(Tnzi.Finance.FinanceModule),
    typeof(Tnzi.Finance.Ai.FinanceAiModule),
    typeof(Tnzi.Finance.Banking.FinanceBankingModule),
    typeof(Tnzi.Finance.Documents.FinanceDocumentsModule),
    typeof(Tnzi.Finance.Payroll.PayrollModule),
    typeof(Tnzi.Finance.Recurring.FinanceRecurringModule),
    typeof(Tnzi.Finance.Tax.Ca.FinanceTaxCaModule),
    typeof(Tnzi.AI.AIModule),
    typeof(Tnzi.AI.Skills.AISkillsModule),
    typeof(Tnzi.AI.Workflow.AIWorkflowModule),
    typeof(Tnzi.AI.Mcp.AIMcpModule),
    typeof(Tnzi.AI.Sandbox.AISandboxModule),
    typeof(Tnzi.AI.Channels.AIChannelsModule),
    typeof(Tnzi.AI.Cli.AICliModule))]
public sealed class AllModulesBootStartupModule : HostingModule;

/// <summary>
/// 启动冒烟用的主 DbContext：带 Identity，框架实体经自动发现注册进来（与消费应用同一条路径）。
/// </summary>
public class AllModulesBootDbContext : Tnzi.Identity.Data.IdentityDbContext<AllModulesBootDbContext>
{
    /// <inheritdoc />
    public AllModulesBootDbContext(
        DbContextOptions<AllModulesBootDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider, multiTenancyOptions)
    {
    }
}
