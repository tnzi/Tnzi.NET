using Tnzi.Hosting;
using Tnzi.Modules;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 把框架里<b>每一个</b>可显式加载的模块都拉进模块图的测试启动模块。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不能直接用 <c>HostingModule</c>：它对 30 个业务模块用的是
/// <c>[OptionalDependsOn]</c>，而可选依赖<b>只排序、不发现</b> —— <c>ModuleLoader</c>
/// 从 <c>HostingModule</c> 出发只会加载 12 个模块。此前的架构门禁正是这么跑的，
/// 于是 Identity / Storage / Finance / AI / Signing 等 30 多个模块从未被审计过一次。
/// </para>
/// <para>
/// 这里刻意用 <c>[DependsOn]</c>（必选）而非 <c>[OptionalDependsOn]</c>：门禁要的是
/// 「全部模块都在场」这一确定事实，可选语义会让覆盖面重新变得依赖加载顺序。
/// </para>
/// <para>
/// <b>类型一律写全限定名。</b>本文件的本质是一份模块清单，全限定名让「少了谁」一眼可见；
/// 且 <c>Tnzi.System</c> 与 BCL 的 <c>System</c> 在 <c>using</c> 下会互相遮蔽，
/// 全限定是这里唯一无歧义的写法（符合命名规范中「消歧时可用全限定名」的例外）。
/// </para>
/// <para>
/// 新增模块时<b>必须</b>把它加进这个列表，否则它不受任何架构门禁约束。
/// <c>ModuleInventoryTests</c> 会反射比对 <c>src</c> 下的模块类与这里的实际图，
/// 漏加即红，不依赖人工记得。
/// </para>
/// </remarks>
// 注意：不要把 HostingModule 写进这个列表 —— 它是 abstract，ModuleLoader 会试图实例化它
// 并抛「must have a parameterless constructor」。本类继承它，Hosting 的基线依赖与
// [OptionalDependsOn] 顺序约束因此已经在图里了。
[DependsOn(
    // 框架层
    typeof(Tnzi.AspNetCore.AspNetCoreModule),
    typeof(Tnzi.EFCore.EFCoreModule),
    typeof(Tnzi.Mapster.MapsterModule),
    typeof(Tnzi.Logging.LoggingModule),
    typeof(Tnzi.Localization.LocalizationModule),
    typeof(Tnzi.Swagger.SwaggerModule),
    typeof(Tnzi.SignalR.SignalRModule),
    typeof(Tnzi.HealthChecks.HealthChecksModule),
    // 基础设施层
    typeof(Tnzi.Redis.RedisCachingModule),
    typeof(Tnzi.RabbitMQ.RabbitMQEventBusModule),
    typeof(Tnzi.Kafka.KafkaEventBusModule),
    typeof(Tnzi.OpenTelemetry.OpenTelemetryModule),
    typeof(Tnzi.Performance.PerformanceModule),
    typeof(Tnzi.Hangfire.HangfireModule),
    typeof(Tnzi.Imaging.ImagingModule),
    // 业务模块
    typeof(Tnzi.Identity.IdentityModule),
    typeof(Tnzi.Identity.Presence.IdentityPresenceModule),
    typeof(Tnzi.Authorization.AuthorizationModule),
    typeof(Tnzi.Storage.StorageModule),
    typeof(Tnzi.Template.TemplateModule),
    typeof(Tnzi.Notification.NotificationModule),
    typeof(Tnzi.Audit.AuditModule),
    typeof(Tnzi.System.SystemModule),
    typeof(Tnzi.Feature.FeatureModule),
    typeof(Tnzi.Chat.ChatModule),
    typeof(Tnzi.Payment.PaymentModule),
    typeof(Tnzi.Documents.DocumentsModule),
    typeof(Tnzi.Signing.SigningModule),
    // Finance 家族
    typeof(Tnzi.Finance.FinanceModule),
    typeof(Tnzi.Finance.Ai.FinanceAiModule),
    typeof(Tnzi.Finance.Banking.FinanceBankingModule),
    typeof(Tnzi.Finance.Documents.FinanceDocumentsModule),
    typeof(Tnzi.Finance.Payroll.PayrollModule),
    typeof(Tnzi.Finance.Recurring.FinanceRecurringModule),
    typeof(Tnzi.Finance.Tax.Ca.FinanceTaxCaModule),
    // AI 家族
    typeof(Tnzi.AI.AIModule),
    typeof(Tnzi.AI.Skills.AISkillsModule),
    typeof(Tnzi.AI.Workflow.AIWorkflowModule),
    typeof(Tnzi.AI.Mcp.AIMcpModule),
    typeof(Tnzi.AI.Rag.AIRagModule),
    typeof(Tnzi.AI.Sandbox.AISandboxModule),
    typeof(Tnzi.AI.Channels.AIChannelsModule),
    typeof(Tnzi.AI.Cli.AICliModule))]
public sealed class AllModulesStartupModule : HostingModule;
