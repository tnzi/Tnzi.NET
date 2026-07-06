namespace Tnzi.Authorization.Permissions;

/// <summary>
/// Framework built-in permission catalogue — the ~68 permission codes the admin
/// shell's 64 routes declare via <c>meta.permission</c> (<c>identity.*</c>,
/// <c>system.*</c>, <c>ai.*</c>, …) plus the <c>Admin.Manage</c> gate that
/// <c>ApiAdminControllerBase</c> requires. Startup <see cref="PermissionDbSeeder"/>
/// upserts them into <c>Auth_FunctionModule</c> / <c>Auth_ModuleFunction</c> so
/// that:
/// <list type="bullet">
///   <item>a super-admin's <c>GetUserPermissionNamesAsync</c> returns the full
///     set (these codes exist, so the catalogue is no longer just 3 app codes);</item>
///   <item>admins can assign them to roles in the normal RoleFunction UI;</item>
///   <item>the front-end's permission-filtered sidebar / route guards line up
///     with codes the backend actually knows about.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why centralised here</b> rather than one provider per framework module:
/// none of the framework business modules (Identity, System, AI, …) depend on
/// <c>Tnzi.Authorization</c>, so they cannot reference
/// <see cref="IPermissionDefinitionProvider"/> without inverting the dependency
/// graph. Authorization is the permission hub, and these codes map 1:1 to the
/// admin route table — keeping them in one file makes the front-end ↔ back-end
/// alignment auditable in a single place. If a module later needs to own its
/// codes, the provider interface can be lifted into core <c>Tnzi</c> (alongside
/// <c>IFunctionAuthorizationService</c>) and this file split per module.
/// </para>
/// <para>
/// <b>Naming</b>: codes use the same lowercase dotted form as the admin routes
/// (<c>user.view</c>) so the front-end's exact-match permission filter matches
/// the codes returned by the backend. Backend permission checks are
/// case-insensitive regardless. Group codes are the module short name
/// (<c>identity</c>); the module's own access code is <c>{group}.view</c>.
/// </para>
/// <para>
/// <b>Category</b>: every code is classified <see cref="PermissionCategory.Business"/>
/// (default, unmarked below) or <see cref="PermissionCategory.Technical"/>
/// (explicit <c>category:</c> argument). Business codes are implicitly granted
/// to members of <c>Authorization:BusinessAdminRoles</c> — the non-technical
/// "business admin" tier; Technical codes (diagnostics, performance, MCP,
/// sandbox, quotas, system parameters, …) stay reachable only via explicit
/// grants or the <c>Authorization:SuperAdminRoles</c> bypass. Deployments that
/// disagree with a default flip it via configuration:
/// <c>Authorization:PermissionCategoryOverrides</c> (code → category) wins over
/// any provider declaration at seed time.
/// </para>
/// </remarks>
public class FrameworkPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // ── Admin gate ──────────────────────────────────────────────────────
        // The single permission ApiAdminControllerBase demands on every admin
        // endpoint. Business category: both admin tiers pass this outer gate;
        // Technical module codes on individual admin controllers then decide
        // who reaches which surface.
        context.AddGroup("admin", "Admin Access");
        context.AddPermission("Admin.Manage", "Access Admin Area", "Outer gate for all /admin endpoints (ApiAdminControllerBase).", parentName: "admin");

        // ── Identity ────────────────────────────────────────────────────────
        context.AddGroup("identity", "Identity");
        context.AddPermission("identity.view", "View Identity", parentName: "identity");
        context.AddPermission("user.view", "View Users", parentName: "identity");
        context.AddPermission("role.view", "View Roles", parentName: "identity");
        context.AddPermission("tenant.view", "View Tenants", parentName: "identity", category: PermissionCategory.Technical);
        context.AddPermission("organization.view", "View Organizations", parentName: "identity");
        context.AddPermission("session.view", "View Sessions", parentName: "identity", category: PermissionCategory.Technical);
        context.AddPermission("identity.loginLog.view", "View Login Logs", parentName: "identity");
        context.AddPermission("identity.loginSecurity.view", "View Login Security", parentName: "identity");

        // ── Authorization ───────────────────────────────────────────────────
        // WHOLE MODULE is Technical: managing "who can do what" (roles →
        // functions, the function/permission catalogue, entity-role data rules)
        // is a security/system concern, not business administration. Every page
        // in this module also needs the function-module catalogue
        // (`GET /admin/modules`, gated by authorization.functionModule.view) to
        // be usable — so splitting the module across tiers produced a
        // half-working role-functions page for business admins (menu visible,
        // catalogue 403). Keeping the module coherently Technical hides it
        // wholesale; business admins still assign existing roles to users via
        // Identity, they just don't redefine what those roles can do.
        context.AddGroup("authorization", "Authorization");
        context.AddPermission("authorization.view", "View Authorization", parentName: "authorization", category: PermissionCategory.Technical);
        context.AddPermission("authorization.functionModule.view", "View Function Modules", parentName: "authorization", category: PermissionCategory.Technical);
        context.AddPermission("authorization.permission.view", "View Permissions", parentName: "authorization", category: PermissionCategory.Technical);
        context.AddPermission("authorization.roleFunction.view", "View Role Functions", parentName: "authorization", category: PermissionCategory.Technical);
        context.AddPermission("authorization.entityRole.view", "View Entity Roles", parentName: "authorization", category: PermissionCategory.Technical);

        // ── System (also hosts dashboard/feature-flag + cross-cutting admin
        //    surfaces like localization/logging/performance/signalr/diagnostics,
        //    which the admin sidebar groups under System) ─────────────────────
        context.AddGroup("system", "System");
        context.AddPermission("system.view", "View System", parentName: "system");
        context.AddPermission("dashboard.view", "View Dashboard", parentName: "system");
        context.AddPermission("feature.view", "View Feature Flags", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.menu.view", "View Menus", parentName: "system");
        context.AddPermission("system.parameter.view", "View Parameters", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.accessLog.view", "View Access Logs", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.dictionary.view", "View Dictionaries", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.scheduledJob.view", "View Scheduled Jobs", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.diagnostics.view", "View Diagnostics", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.health.view", "View Health Checks", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.localization.view", "View Localization", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.log.view", "View Logs", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.performance.view", "View Performance", parentName: "system", category: PermissionCategory.Technical);
        context.AddPermission("system.signalr.view", "View SignalR", parentName: "system", category: PermissionCategory.Technical);

        // ── Storage ─────────────────────────────────────────────────────────
        context.AddGroup("storage", "Storage");
        context.AddPermission("storage.view", "View Storage", parentName: "storage");
        context.AddPermission("storage.file.view", "View Files", parentName: "storage");
        context.AddPermission("storage.chunk.view", "View Chunks", parentName: "storage", category: PermissionCategory.Technical);
        context.AddPermission("storage.version.view", "View Versions", parentName: "storage", category: PermissionCategory.Technical);

        // ── Audit ───────────────────────────────────────────────────────────
        context.AddGroup("audit", "Audit");
        context.AddPermission("audit.view", "View Audit", parentName: "audit");
        context.AddPermission("audit.log.view", "View Audit Logs", parentName: "audit");
        context.AddPermission("audit.operation.view", "View Audit Operations", parentName: "audit");

        // ── Notification ────────────────────────────────────────────────────
        context.AddGroup("notification", "Notification");
        context.AddPermission("notification.view", "View Notification", parentName: "notification");
        context.AddPermission("notification.message.view", "View Notification Messages", parentName: "notification");
        context.AddPermission("notification.subscription.view", "View Subscriptions", parentName: "notification");
        context.AddPermission("notification.template.view", "View Notification Templates", parentName: "notification");

        // ── Chat ────────────────────────────────────────────────────────────
        context.AddGroup("chat", "Chat");
        context.AddPermission("chat.view", "View Chat", parentName: "chat");
        context.AddPermission("chat.session.view", "View Chat Sessions", parentName: "chat");

        // ── Payment ─────────────────────────────────────────────────────────
        context.AddGroup("payment", "Payment");
        context.AddPermission("payment.view", "View Payment", parentName: "payment");
        context.AddPermission("payment.order.view", "View Orders", parentName: "payment");
        context.AddPermission("payment.refund.view", "View Refunds", parentName: "payment");
        context.AddPermission("payment.subscription.view", "View Payment Subscriptions", parentName: "payment");
        context.AddPermission("payment.invoice.view", "View Invoices", parentName: "payment");
        context.AddPermission("payment.promotion.view", "View Promotions", parentName: "payment");
        context.AddPermission("payment.statistics.view", "View Payment Statistics", parentName: "payment");

        // ── Finance ─────────────────────────────────────────────────────────
        context.AddGroup("finance", "Finance");
        context.AddPermission("finance.view", "View Finance", parentName: "finance");
        context.AddPermission("finance.account.view", "View Chart of Accounts", parentName: "finance");
        context.AddPermission("finance.journal.view", "View Journal Entries", parentName: "finance");
        context.AddPermission("finance.rate.view", "View Exchange Rates", parentName: "finance");
        context.AddPermission("finance.fiscalYear.view", "View Fiscal Years", parentName: "finance");
        context.AddPermission("finance.report.view", "View Financial Reports", parentName: "finance");
        context.AddPermission("finance.customer.view", "View Customers", parentName: "finance");
        context.AddPermission("finance.vendor.view", "View Vendors", parentName: "finance");
        context.AddPermission("finance.item.view", "View Items", parentName: "finance");
        context.AddPermission("finance.tax.view", "View Taxes", parentName: "finance");
        context.AddPermission("finance.document.view", "View Finance Documents", parentName: "finance");

        // ── Template ────────────────────────────────────────────────────────
        context.AddGroup("template", "Template");
        context.AddPermission("template.view", "View Template", parentName: "template");
        context.AddPermission("template.template.view", "View Templates", parentName: "template");
        context.AddPermission("template.layout.view", "View Layouts", parentName: "template");

        // ── AI ──────────────────────────────────────────────────────────────
        context.AddGroup("ai", "AI");
        context.AddPermission("ai.view", "View AI", parentName: "ai");
        context.AddPermission("ai.agent.view", "View Agents", parentName: "ai");
        context.AddPermission("ai.agentRun.view", "View Agent Runs", parentName: "ai");
        context.AddPermission("ai.persona.view", "View Personas", parentName: "ai");
        context.AddPermission("ai.provider.view", "View Providers", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.skill.view", "View Skills", parentName: "ai");
        context.AddPermission("ai.knowledge.view", "View Knowledge", parentName: "ai");
        context.AddPermission("ai.mcp.view", "View MCP Servers", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.quota.view", "View Quotas", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.workflow.view", "View Workflows", parentName: "ai");
        context.AddPermission("ai.workflowRun.view", "View Workflow Runs", parentName: "ai");
        context.AddPermission("ai.usage.view", "View AI Usage", parentName: "ai");
        context.AddPermission("ai.evaluation.view", "View Evaluations", parentName: "ai");
        context.AddPermission("ai.channels.view", "View Channels", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.sandbox.view", "View Sandbox", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.permissions.view", "View AI Permissions", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.thread.view", "View Threads", parentName: "ai");
        context.AddPermission("ai.sql.execute", "Execute AI SQL Queries", parentName: "ai", category: PermissionCategory.Technical);
    }
}
