using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AI.Channels.Permissions;
using Tnzi.AI.Channels;
using Tnzi.AI.Mcp.Permissions;
using Tnzi.AI.Mcp;
using Tnzi.AI.Permissions;
using Tnzi.AI.Rag.Permissions;
using Tnzi.AI.Rag;
using Tnzi.AI.Sandbox.Permissions;
using Tnzi.AI.Sandbox;
using Tnzi.AI.Skills.Permissions;
using Tnzi.AI.Skills;
using Tnzi.AI.Workflow.Permissions;
using Tnzi.AI.Workflow;
using Tnzi.AI;
using Tnzi.AspNetCore.Permissions;
using Tnzi.AspNetCore;
using Tnzi.Audit.Permissions;
using Tnzi.Audit;
using Tnzi.Authorization.Permissions;
using Tnzi.Authorization;
using Tnzi.Chat.Permissions;
using Tnzi.Chat;
using Tnzi.Documents.Signing.Permissions;
using Tnzi.Documents.Signing;
using Tnzi.Feature.Permissions;
using Tnzi.Feature;
using Tnzi.Finance.Banking.Permissions;
using Tnzi.Finance.Banking;
using Tnzi.Finance.Payroll.Permissions;
using Tnzi.Finance.Payroll;
using Tnzi.Finance.Permissions;
using Tnzi.Finance.Recurring.Permissions;
using Tnzi.Finance.Recurring;
using Tnzi.Finance;
using Tnzi.Hangfire.Permissions;
using Tnzi.Hangfire;
using Tnzi.HealthChecks.Permissions;
using Tnzi.HealthChecks;
using Tnzi.Identity.Permissions;
using Tnzi.Identity;
using Tnzi.Localization.Permissions;
using Tnzi.Localization;
using Tnzi.Modules;
using Tnzi.Notification.Permissions;
using Tnzi.Notification;
using Tnzi.Payment.Permissions;
using Tnzi.Payment;
using Tnzi.Performance.Permissions;
using Tnzi.Performance;
using Tnzi.Security.Authorization;
using Tnzi.SignalR.Permissions;
using Tnzi.SignalR;
using Tnzi.Storage.Permissions;
using Tnzi.Storage;
using Tnzi.System.Permissions;
using Tnzi.System.Settings;
using Tnzi.System;
using Tnzi.Template.Permissions;
using Tnzi.Template;


namespace Tnzi.PermissionCatalogue.Tests;

/// <summary>
/// 每个声明了权限目录的模块，必须**在自己的 <c>ConfigureServicesAsync</c> 里注册**
/// 那个 <see cref="IPermissionDefinitionProvider"/>。
/// </summary>
/// <remarks>
/// ★ 这条门禁是被一次真实的疏漏逼出来的（2026-07-31，<c>SigningModule</c>）：权限类写好了、
/// 控制器上的 <c>[ApiAuthorize]</c> 也引用了那些码，唯独模块没注册 provider。后果是
/// <c>PermissionDbSeeder</c> 收集不到它，码永远不会被播种，于是**每个管理端点恒 403，
/// 而没有任何一处会告诉你为什么** —— 看起来像"权限没配好"，实际上那个码根本不存在。
///
/// 更糟的是它对既有测试完全不可见：pact 直接 <c>new XxxPermissions()</c> 求值，
/// 从不经过 DI，所以码数、分组、类别全部照常绿着。
///
/// 本测试从 DI 那一侧问同一个问题：把模块真的跑一遍 <c>ConfigureServicesAsync</c>，
/// 看容器里到底有没有它。
/// </remarks>
public class PermissionProviderRegistrationTests
{
    /// <summary>
    /// 待检模块 → 它应当注册的 provider 类型。与 <see cref="PermissionCataloguePactTests"/>
    /// 的 provider 清单一一对应（那边验"声明了什么"，这边验"注册了没有"）。
    /// </summary>
    public static TheoryData<Type, Type> ModulesWithCatalogues() => new()
    {
        { typeof(IdentityModule), typeof(IdentityPermissions) },
        { typeof(AuthorizationModule), typeof(AuthorizationPermissions) },
        { typeof(SystemModule), typeof(SystemPermissions) },
        { typeof(StorageModule), typeof(StoragePermissions) },
        { typeof(AuditModule), typeof(AuditPermissions) },
        { typeof(NotificationModule), typeof(NotificationPermissions) },
        { typeof(ChatModule), typeof(ChatPermissions) },
        { typeof(PaymentModule), typeof(PaymentPermissions) },
        { typeof(FinanceModule), typeof(FinancePermissions) },
        { typeof(PayrollModule), typeof(PayrollPermissions) },
        { typeof(TemplateModule), typeof(TemplatePermissions) },
        { typeof(AIModule), typeof(AIPermissions) },
        { typeof(SigningModule), typeof(SigningPermissions) },
        { typeof(AspNetCoreModule), typeof(AspNetCorePermissions) },
        { typeof(HangfireModule), typeof(HangfirePermissions) },
        { typeof(LocalizationModule), typeof(LocalizationPermissions) },
        { typeof(PerformanceModule), typeof(PerformancePermissions) },
        { typeof(SignalRModule), typeof(SignalRPermissions) },
        { typeof(HealthChecksModule), typeof(HealthChecksPermissions) },
        { typeof(FeatureModule), typeof(FeaturePermissions) },
        { typeof(FinanceBankingModule), typeof(FinanceBankingPermissions) },
        { typeof(FinanceRecurringModule), typeof(FinanceRecurringPermissions) },
        { typeof(AIMcpModule), typeof(AIMcpPermissions) },
        { typeof(AISkillsModule), typeof(AISkillsPermissions) },
        { typeof(AIWorkflowModule), typeof(AIWorkflowPermissions) },
        { typeof(AIRagModule), typeof(RagPermissions) },
        { typeof(AISandboxModule), typeof(SandboxPermissions) },
        { typeof(AIChannelsModule), typeof(ChannelsPermissions) },

        // 一个模块可以注册不止一个 provider。这条是**动态**目录：按已注册的设置分组
        // 派生权限码，所以它带构造依赖、也不在 PermissionCataloguePactTests 的静态
        // 清单里（那边是 `new XxxPermissions()` 直接求值的）。注册与否同样要守。
        { typeof(SystemModule), typeof(SettingsPermissionDefinitionProvider) },
    };

    [Theory]
    [MemberData(nameof(ModulesWithCatalogues))]
    public void Module_registers_its_permission_definition_provider(Type moduleType, Type providerType)
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        var module = (ITnziModule)Activator.CreateInstance(moduleType)!;
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        var registered = services
            .Where(d => d.ServiceType == typeof(IPermissionDefinitionProvider))
            .Select(d => d.ImplementationType)
            .ToList();

        registered.ShouldContain(
            providerType,
            $"{moduleType.Name} declares a permission catalogue ({providerType.Name}) but never registers it. " +
            "Nothing seeds those codes, so every [ApiAuthorize] that references them denies forever.");
    }

    [Fact]
    public void Every_provider_the_pact_declares_is_also_checked_for_registration()
    {
        // ★ 与 PermissionCataloguePactTests.AllProviders 对账，而**不是**扫程序集。
        //   扫程序集的范围由本测试自己的表决定：删掉某一行，那个程序集也就一起
        //   离开了扫描范围，于是"少覆盖了一个模块"这件事自己把自己藏了起来。
        //   拿另一份清单当尺子，删行就会立刻现形。
        var checkedProviders = ModulesWithCatalogues().Select(row => (Type)row[1]).ToHashSet();
        var declared = PermissionCataloguePactTests.AllProviders.Values.Select(p => p.GetType()).ToList();

        var missing = declared.Where(t => !checkedProviders.Contains(t)).Select(t => t.Name).ToList();
        missing.ShouldBeEmpty(
            "These providers are in the pact's catalogue but nothing checks that their module registers them: " +
            $"{string.Join(", ", missing)}. A declared-but-unregistered catalogue means every [ApiAuthorize] " +
            "referencing those codes denies forever.");
    }
}
