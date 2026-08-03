using System.Reflection;
using Tnzi.AI.Channels.Permissions;
using Tnzi.Documents.Signing.Permissions;
using Tnzi.AspNetCore.Mvc;
using Tnzi.Authorization.Permissions;
using Tnzi.Security.Authorization;
using Tnzi.TestBase;
using Tnzi.AI.Mcp.Permissions;
using Tnzi.AI.Permissions;
using Tnzi.AI.Rag.Permissions;
using Tnzi.AI.Sandbox.Permissions;
using Tnzi.AI.Skills.Permissions;
using Tnzi.AI.Workflow.Permissions;
using Tnzi.AspNetCore.Permissions;
using Tnzi.Audit.Permissions;
using Tnzi.Chat.Permissions;
using Tnzi.Feature.Permissions;
using Tnzi.Finance.Permissions;
using Tnzi.Finance.Banking.Permissions;
using Tnzi.Finance.Recurring.Permissions;
using Tnzi.Finance.Payroll.Permissions;
using Tnzi.Hangfire.Permissions;
using Tnzi.HealthChecks.Permissions;
using Tnzi.Identity.Permissions;
using Tnzi.Localization.Permissions;
using Tnzi.Notification.Permissions;
using Tnzi.Payment.Permissions;
using Tnzi.Performance.Permissions;
using Tnzi.SignalR.Permissions;
using Tnzi.Storage.Permissions;
using Tnzi.System.Permissions;
using Tnzi.Template.Permissions;

namespace Tnzi.PermissionCatalogue.Tests;

/// <summary>
/// Ecosystem-level pact over the framework permission catalogue. Every module
/// declares its own codes in-module (docs/coding-standards/permissions.md);
/// this test aggregates ALL framework providers - exactly what a host loading
/// every module would seed - and locks the totals against drift. If a
/// front-end route adds a code without a matching module declaration, a
/// super-admin would stop seeing that menu and a normal user could never be
/// granted it; the count + key-code assertions make that kind of regression a
/// failing test instead of a silent gap.
/// </summary>
/// <remarks>
/// Module-loading gating is structural now: a host that does not load a
/// module never registers its provider, so its codes are never seeded. There
/// is no runtime module-set check left to test - the per-provider isolation
/// tests below cover the same guarantee.
/// </remarks>
public class PermissionCataloguePactTests
{
    /// <summary>Every framework permission provider, keyed by owner name for diagnostics.</summary>
    /// <summary>
    /// 全部权限 provider（模块短名 → 实例）。<see cref="PermissionProviderRegistrationTests"/>
    /// 拿它当"到底有哪些 provider"的单一真值源——否则那边只能靠扫程序集，而扫的范围
    /// 又恰好由它自己的清单决定，删一行等于连带把那个程序集移出扫描范围。
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IPermissionDefinitionProvider> AllProviders =
        new Dictionary<string, IPermissionDefinitionProvider>
        {
            ["Identity"] = new IdentityPermissions(),
            ["Authorization"] = new AuthorizationPermissions(),
            ["System"] = new SystemPermissions(),
            ["AspNetCore"] = new AspNetCorePermissions(),
            ["Hangfire"] = new HangfirePermissions(),
            ["Localization"] = new LocalizationPermissions(),
            ["Performance"] = new PerformancePermissions(),
            ["SignalR"] = new SignalRPermissions(),
            ["HealthChecks"] = new HealthChecksPermissions(),
            ["Feature"] = new FeaturePermissions(),
            ["Storage"] = new StoragePermissions(),
            ["Audit"] = new AuditPermissions(),
            ["Notification"] = new NotificationPermissions(),
            ["Chat"] = new ChatPermissions(),
            ["Payment"] = new PaymentPermissions(),
            ["Finance"] = new FinancePermissions(),
            ["Finance.Banking"] = new FinanceBankingPermissions(),
            ["Finance.Recurring"] = new FinanceRecurringPermissions(),
            ["Finance.Payroll"] = new PayrollPermissions(),
            ["Template"] = new TemplatePermissions(),
            ["AI"] = new AIPermissions(),
            ["AI.Mcp"] = new AIMcpPermissions(),
            ["AI.Skills"] = new AISkillsPermissions(),
            ["AI.Workflow"] = new AIWorkflowPermissions(),
            ["AI.Rag"] = new RagPermissions(),
            ["AI.Sandbox"] = new SandboxPermissions(),
            ["AI.Channels"] = new ChannelsPermissions(),
            ["Documents.Signing"] = new SigningPermissions(),
        };

    private static PermissionDefinitionContext BuildContext()
    {
        var context = new PermissionDefinitionContext();
        foreach (var provider in AllProviders.Values)
        {
            provider.Define(context);
        }
        return context;
    }

    [Fact]
    public void Aggregated_catalogue_declares_expected_codes_without_duplicates()
    {
        var context = BuildContext();
        var codes = context.Permissions.Values.Select(p => p.Name).ToList();

        // No duplicate permission codes.
        codes.Count.ShouldBe(codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Locked counts - bump these deliberately when adding/removing codes so
        // an accidental drift (front-end route added without a backend code, or
        // vice versa) shows up as a failing test. 251 = the operation-level
        // catalogue: every managed surface declares .view plus the write
        // actions its admin endpoints actually expose (incl. the userFunction
        // direct-grant surface added 2026-07-10, the finance.reconciliation
        // bank-reconciliation surface added 2026-07-11, the
        // finance.revaluation view/execute pair added 2026-07-13, the
        // payroll group's 14 codes added with the Payroll sub-module 2026-07-13,
        // the 23 finance P3 output/ingestion codes added 2026-07-13:
        // bankAccount/bankFeed/receipt/partyBank crud (4 each), check
        // view/create/update (3), and eft view/create/update + download (4),
        // the finance.balanceSummary view/execute pair added 2026-07-13
        // with the account-period-balance summary surface, and chat.use
        // added 2026-07-14 as the chat group's deny-by-default use gate;
        // minus the ai.persona crud codes retired 2026-07-22 with the persona
        // pool, and the system.menu crud codes retired 2026-07-22 with the
        // Sys_Menu override layer; plus the ai.mcpServer view/update/delete
        // trio added 2026-07-23 when the MCP Server sub-module took ownership of
        // its admin surfaces (self-hosted server management, split off from the
        // ai.mcp external-client-registry codes it previously borrowed); plus
        // the finance.estimate and finance.purchaseOrder crud sets added
        // 2026-07-25 (8 codes) - quoting and ordering happen BEFORE anything is
        // an accounting fact, so the people who do them get their own codes
        // rather than the posting rights of finance.document.*.
        // 又加了 finance.bankRule crud（4 码，2026-07-25）——能改银行规则的人等于
        // 能决定钱自动记进哪个科目，与"能看流水、能确认匹配"不是一回事。
        // 再加 finance.attachment 与 finance.comment 各 view/create/delete
        // （6 码，2026-07-25）——能看单据不等于该看得到内部讨论。
        // 又加 payment.refund.create（1 码，2026-07-31）——客服代客发起退款，
        // 与"审批一笔已提交的退款申请"是两种不同的能力。
        // 再加电子签署 9 码（1 组，2026-07-31）——signing.view + template/request 各一套
        // crud。收件人签署面不在目录里：它是匿名的、凭一次性令牌进入，给客户和对家发
        // 账号才能签字等于让电子签比纸笔更麻烦。
        codes.Count.ShouldBe(290);
        context.Groups.Count.ShouldBe(13);
    }

    [Fact]
    public void Every_code_is_declared_by_exactly_one_module()
    {
        // AddGroup/AddPermission are first-wins, which would silently mask a
        // duplicate code declared by two different modules. Run each provider
        // into its OWN context and assert pairwise disjoint code sets, so
        // ownership stays unambiguous ecosystem-wide.
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, provider) in AllProviders)
        {
            var context = new PermissionDefinitionContext();
            provider.Define(context);
            foreach (var code in context.Permissions.Keys)
            {
                owners.TryGetValue(code, out var existing).ShouldBeFalse(
                    $"'{code}' is declared by both {existing} and {name} - a code must have exactly one owning module.");
                owners[code] = name;
            }
        }
    }

    [Fact]
    public void Shared_groups_are_declared_consistently_by_every_contributor()
    {
        // "system" / "ai" are shared groups: several modules declare them and
        // rely on AddGroup's first-wins idempotency. Every contributor must use
        // identical arguments, otherwise the effective display name / default
        // category would depend on module load order.
        var declarations = new Dictionary<string, List<(string Owner, PermissionGroupDefinition Group)>>();
        foreach (var (name, provider) in AllProviders)
        {
            var context = new PermissionDefinitionContext();
            provider.Define(context);
            foreach (var group in context.Groups.Values)
            {
                if (!declarations.TryGetValue(group.Name, out var list))
                {
                    declarations[group.Name] = list = new List<(string, PermissionGroupDefinition)>();
                }
                list.Add((name, group));
            }
        }

        foreach (var (groupName, list) in declarations.Where(d => d.Value.Count > 1))
        {
            var first = list[0].Group;
            foreach (var (owner, group) in list.Skip(1))
            {
                group.DisplayName.ShouldBe(first.DisplayName,
                    $"Group '{groupName}': {owner} declares a different DisplayName than {list[0].Owner}.");
                group.DefaultCategory.ShouldBe(first.DefaultCategory,
                    $"Group '{groupName}': {owner} declares a different DefaultCategory than {list[0].Owner}.");
            }
        }
    }

    [Fact]
    public void Providers_declare_only_their_own_groups()
    {
        // In-module declaration is the gating mechanism: a provider must not
        // smuggle in another module's group (that would re-create the old
        // centralised catalogue and seed codes for modules the host never
        // loaded). Representative check on the hub module itself.
        var context = new PermissionDefinitionContext();
        new AuthorizationPermissions().Define(context);

        context.Groups.Count.ShouldBe(1);
        context.Groups.ContainsKey("authorization").ShouldBeTrue();
        context.Permissions.ContainsKey("authorization.roleFunction.view").ShouldBeTrue();
        context.Permissions.ContainsKey("user.view").ShouldBeFalse();
        context.Permissions.ContainsKey("finance.account.view").ShouldBeFalse();
        context.Permissions.ContainsKey("ai.agent.view").ShouldBeFalse();
    }

    [Theory]
    // Baseline infrastructure must NOT be grantable matrix rows: clearing a
    // role would brick its members (no way to even self-query permissions,
    // no landing page). "Enter the admin area" is authentication + any
    // grant; the dashboard is permission-free by design.
    [InlineData("Admin.Manage")]
    [InlineData("dashboard.view")]
    public void Baseline_infrastructure_is_not_declared_as_grantable(string code)
    {
        var context = BuildContext();
        context.Permissions.ContainsKey(code).ShouldBeFalse(
            $"'{code}' is baseline infrastructure and must not appear in the grantable catalogue.");
    }

    [Theory]
    // Technical: system/ops surfaces - assignment UIs render a warning badge.
    [InlineData("tenant.view", PermissionCategory.Technical)]
    [InlineData("session.view", PermissionCategory.Technical)]
    // Security-audit monitoring under identity: same genre as accessLog.
    [InlineData("identity.loginLog.view", PermissionCategory.Technical)]
    [InlineData("identity.loginSecurity.view", PermissionCategory.Technical)]
    // Whole Authorization module is Technical - managing roles/permissions is a
    // security concern; the uniform badge tells operators these codes hand out
    // the authorization matrix itself.
    [InlineData("authorization.view", PermissionCategory.Technical)]
    [InlineData("authorization.functionModule.view", PermissionCategory.Technical)]
    [InlineData("authorization.permission.view", PermissionCategory.Technical)]
    [InlineData("authorization.roleFunction.view", PermissionCategory.Technical)]
    [InlineData("authorization.userFunction.view", PermissionCategory.Technical)]
    [InlineData("authorization.entityRole.view", PermissionCategory.Technical)]
    // Whole system group is Technical (group default): ops/infrastructure surfaces.
    [InlineData("system.view", PermissionCategory.Technical)]
    [InlineData("feature.view", PermissionCategory.Technical)]
    [InlineData("system.parameter.view", PermissionCategory.Technical)]
    [InlineData("system.appearance.view", PermissionCategory.Technical)]
    [InlineData("system.appearance.update", PermissionCategory.Technical)]
    [InlineData("system.accessLog.view", PermissionCategory.Technical)]
    [InlineData("system.scheduledJob.view", PermissionCategory.Technical)]
    [InlineData("system.diagnostics.view", PermissionCategory.Technical)]
    [InlineData("system.health.view", PermissionCategory.Technical)]
    [InlineData("system.localization.view", PermissionCategory.Technical)]
    [InlineData("system.log.view", PermissionCategory.Technical)]
    [InlineData("system.performance.view", PermissionCategory.Technical)]
    [InlineData("system.signalr.view", PermissionCategory.Technical)]
    [InlineData("storage.chunk.view", PermissionCategory.Technical)]
    [InlineData("storage.version.view", PermissionCategory.Technical)]
    // Request-level audit is technical monitoring (paths/status/duration).
    [InlineData("audit.log.view", PermissionCategory.Technical)]
    // AI engineering/ops surfaces: run monitors, DAG workflows, evaluations.
    [InlineData("ai.agentRun.view", PermissionCategory.Technical)]
    [InlineData("ai.agentRun.execute", PermissionCategory.Technical)]
    [InlineData("ai.workflow.view", PermissionCategory.Technical)]
    [InlineData("ai.workflow.execute", PermissionCategory.Technical)]
    [InlineData("ai.workflowRun.view", PermissionCategory.Technical)]
    [InlineData("ai.evaluation.view", PermissionCategory.Technical)]
    [InlineData("ai.evaluation.execute", PermissionCategory.Technical)]
    [InlineData("ai.provider.view", PermissionCategory.Technical)]
    [InlineData("ai.mcp.view", PermissionCategory.Technical)]
    [InlineData("ai.quota.view", PermissionCategory.Technical)]
    [InlineData("ai.channels.view", PermissionCategory.Technical)]
    [InlineData("ai.sandbox.view", PermissionCategory.Technical)]
    [InlineData("ai.permissions.view", PermissionCategory.Technical)]
    [InlineData("ai.sql.execute", PermissionCategory.Technical)]
    // Dictionaries share the /admin/settings endpoint (gated by
    // system.parameter.view); the code stays registered and keeps the
    // Technical badge alongside it.
    [InlineData("system.dictionary.view", PermissionCategory.Technical)]
    // Business spot checks: everyday business surfaces carry no badge.
    [InlineData("user.view", PermissionCategory.Business)]
    [InlineData("role.view", PermissionCategory.Business)]
    [InlineData("organization.view", PermissionCategory.Business)]
    [InlineData("storage.file.view", PermissionCategory.Business)]
    [InlineData("audit.view", PermissionCategory.Business)]
    [InlineData("audit.operation.view", PermissionCategory.Business)]
    [InlineData("notification.template.view", PermissionCategory.Business)]
    [InlineData("template.template.view", PermissionCategory.Business)]
    [InlineData("chat.session.view", PermissionCategory.Business)]
    [InlineData("finance.account.view", PermissionCategory.Business)]
    [InlineData("payroll.employee.view", PermissionCategory.Business)]
    [InlineData("payment.order.view", PermissionCategory.Business)]
    [InlineData("ai.agent.view", PermissionCategory.Business)]
    [InlineData("ai.thread.view", PermissionCategory.Business)]
    [InlineData("ai.usage.view", PermissionCategory.Business)]
    public void Catalogue_classifies_code_with_expected_category(string code, PermissionCategory expected)
    {
        var context = BuildContext();
        context.Permissions[code].Category.ShouldBe(expected);
    }

    [Fact]
    public void Technical_code_count_is_locked()
    {
        var context = BuildContext();
        var technical = context.Permissions.Values
            .Where(p => p.Category == PermissionCategory.Technical)
            .Select(p => p.Name)
            .ToList();

        // Deliberate-bump lock, same rationale as the total count: a code
        // silently flipping category erases the warning badge assignment UIs
        // rely on to flag ops/dangerous surfaces. 87 = the 2026-07-07 audit
        // sweep ("a business admin can't read it → Technical"): whole system
        // group (the global appearance snapshot + the diagnostics/signalr
        // execute actions), identity login logs/security, request-level audit
        // logs, AI run monitors / workflows / evaluations; plus the 2026-07-10
        // userFunction direct-grant pair (authorization group default
        // Technical). The system.menu crud codes were removed 2026-07-22 with
        // the Sys_Menu override layer; the ai.mcpServer view/update/delete trio
        // (self-hosted MCP server ops surface) was added 2026-07-23.
        technical.Count.ShouldBe(90);
    }

    [Theory]
    [InlineData("identity.view")]
    [InlineData("user.view")]
    [InlineData("role.view")]
    [InlineData("tenant.view")]
    [InlineData("authorization.roleFunction.view")]
    [InlineData("system.view")]
    [InlineData("feature.view")]
    [InlineData("storage.file.view")]
    [InlineData("audit.log.view")]
    [InlineData("notification.message.view")]
    [InlineData("chat.session.view")]
    [InlineData("payment.order.view")]
    [InlineData("finance.account.view")]
    [InlineData("finance.journal.view")]
    [InlineData("finance.report.view")]
    [InlineData("payroll.employee.view")]
    [InlineData("payroll.config.view")]
    [InlineData("payroll.run.view")]
    [InlineData("template.layout.view")]
    [InlineData("ai.agent.view")]
    [InlineData("ai.thread.view")]
    public void Catalogue_includes_admin_route_permission_code(string code)
    {
        var context = BuildContext();
        context.Permissions.ContainsKey(code).ShouldBeTrue(
            $"The aggregated catalogue must declare '{code}' (referenced by an admin route's meta.permission).");
    }

    [Theory]
    // Operation-level codes: CRUD triples for managed entities...
    [InlineData("user.create")]
    [InlineData("user.update")]
    [InlineData("user.delete")]
    [InlineData("role.delete")]
    [InlineData("finance.account.create")]
    [InlineData("finance.document.update")]
    [InlineData("notification.template.delete")]
    [InlineData("ai.agent.create")]
    // ...trigger-style operations get .execute...
    [InlineData("ai.agent.execute")]
    [InlineData("ai.workflow.execute")]
    [InlineData("ai.agentRun.execute")]
    [InlineData("system.scheduledJob.execute")]
    [InlineData("ai.evaluation.execute")]
    // ...payroll's run lifecycle rides .update; country-pack seeding is a
    // trigger with its own .execute (the only pack write surface).
    [InlineData("payroll.run.update")]
    [InlineData("payroll.pack.execute")]
    // ...and permission granting (role-scoped and user-direct) is the
    // dedicated assign action.
    [InlineData("authorization.roleFunction.assign")]
    [InlineData("authorization.userFunction.assign")]
    public void Catalogue_includes_operation_level_code(string code)
    {
        var context = BuildContext();
        context.Permissions.ContainsKey(code).ShouldBeTrue(
            $"The aggregated catalogue must declare the operation-level code '{code}'.");
    }

    [Theory]
    // Read-only surfaces must NOT grow phantom write codes: nobody could ever
    // exercise them and the matrix UI would render dead checkboxes.
    [InlineData("audit.log.create")]
    [InlineData("system.diagnostics.update")]
    [InlineData("system.performance.delete")]
    [InlineData("payment.statistics.create")]
    [InlineData("finance.report.update")]
    // Country packs are seed-only (.execute); crud codes would be dead rows.
    [InlineData("payroll.pack.create")]
    [InlineData("payroll.pack.delete")]
    [InlineData("ai.usage.delete")]
    [InlineData("dashboard.update")]
    // Sessions cannot be created by admins; roleFunction/userFunction use
    // .assign, not crud.
    [InlineData("session.create")]
    [InlineData("authorization.roleFunction.create")]
    [InlineData("authorization.roleFunction.update")]
    [InlineData("authorization.userFunction.create")]
    [InlineData("authorization.userFunction.update")]
    public void Catalogue_does_not_declare_write_codes_for_read_only_surfaces(string code)
    {
        var context = BuildContext();
        context.Permissions.ContainsKey(code).ShouldBeFalse(
            $"'{code}' must not exist - the surface exposes no such admin operation.");
    }

    [Fact]
    public void Every_admin_controller_referenced_code_is_declared_by_some_provider()
    {
        // 全框架不变量：admin 控制器 [ApiAuthorize] 引用的每个权限码都必须由某个
        // provider 声明，否则该门永远无人可被授予（端点恒 403）——三层 AND 门里
        // 编译器与写端点约定测试都不校验的一环。扫描集 = 全部 provider 所在程序集
        // （控制器与其权限声明同程序集）；对聚合目录校验，兼容合法的跨模块复用码
        // （如 receipts convert 复用 finance.document.create）。基类 Admin.Manage
        // 经 inherit:false 排除；运行时派生的动态码（{group}.settings.* 等）不经
        // 控制器 attribute 引用，不在扫描面。
        var declared = BuildContext().Permissions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assemblies = AllProviders.Values.Select(p => p.GetType().Assembly).Distinct().ToList();

        var controllers = assemblies
            .SelectMany(a => a.SafeGetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ApiAdminControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.FullName)
            .ToList();

        // 哨兵：扫描失效（基类搬家/程序集缺失）时显式失败而非空转全绿。
        controllers.Count.ShouldBeGreaterThanOrEqualTo(80);

        var violations = new List<string>();
        foreach (var controller in controllers)
        {
            var referenced = controller.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: false)
                .Concat(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: false)))
                .Select(a => a.PermissionName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var code in referenced)
            {
                if (!declared.Contains(code!))
                    violations.Add($"{controller.Name} references '{code}' which no provider declares - the gate could never be granted (endpoint is permanently 403).");
            }
        }

        violations.ShouldBeEmpty(string.Join("\n", violations));
    }

    [Fact]
    public void Write_action_codes_inherit_their_surface_category()
    {
        var context = BuildContext();
        // Technical surfaces keep the badge on every action code...
        context.Permissions["tenant.create"].Category.ShouldBe(PermissionCategory.Technical);
        context.Permissions["system.parameter.update"].Category.ShouldBe(PermissionCategory.Technical);
        context.Permissions["ai.mcp.delete"].Category.ShouldBe(PermissionCategory.Technical);
        context.Permissions["authorization.roleFunction.assign"].Category.ShouldBe(PermissionCategory.Technical);
        // ...business surfaces stay unbadged.
        context.Permissions["user.create"].Category.ShouldBe(PermissionCategory.Business);
        context.Permissions["finance.journal.update"].Category.ShouldBe(PermissionCategory.Business);
    }
}
