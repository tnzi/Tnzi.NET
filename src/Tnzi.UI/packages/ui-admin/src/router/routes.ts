import { defineComponent, h } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import AdminShellRoot from './AdminShellRoot.vue'

/**
 * Default admin routes — Phase 3.38: all 28 module pages registered.
 *
 * Placeholder components are kept for login/forbidden.
 * All admin pages use vue-router's async component syntax (`() => import(...)`)
 * for code splitting — vue-router handles the async loading + internal Suspense
 * boundary itself; wrapping in `defineAsyncComponent` triggers a warning and
 * conflicts with router-level Suspense (see Phase H3 regression).
 * The `/admin` parent renders {@link AdminShellRoot} so every child page
 * inherits the standard TAdminShell layout (sidebar + header + content).
 */

const ForbiddenPlaceholder = defineComponent({
  name: 'ForbiddenPlaceholder',
  render: () => h('div', '403 Forbidden'),
})

export const defaultAdminRoutes: RouteRecordRaw[] = [
  {
    // Soybean parity: `/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat)?`.
    // The module param defaults to `pwd-login` inside the page component; the
    // regex constraint rejects unknown values and routes them to a 404. Order
    // matters — the path-param route must come before the bare `/login` redirect.
    path: '/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat|two-factor)?',
    name: 'login',
    component: () => import('../pages/login/index.vue'),
    meta: { requiresAuth: false, title: 'Login' },
  },
  {
    path: '/403',
    name: 'forbidden',
    component: ForbiddenPlaceholder,
    meta: { requiresAuth: false, title: '403' },
  },
  {
    path: '/admin',
    name: 'admin-root',
    component: AdminShellRoot,
    redirect: '/admin/workbench',
    meta: { requiresAuth: true, title: 'Admin' },
    children: [
      // ── Workbench (default landing page) ─────────────────────
      // Default `meta.order` values on top-level modules (step 10 leaves
      // room for consumer-injected entries). Workbench sits at 0 so it
      // always lands first; consumer modules registered via `addModules`
      // with `meta.order` in 1..99 slot between Workbench and Identity,
      // and 200+ for after the last framework module — no consumer
      // mutation required. Override per-route via
      // `defineAdminApp({ routeOrders: { 'workbench': 5 } })`.
      {
        path: 'workbench',
        name: 'workbench',
        component: () => import('../pages/dashboard/Workbench.vue'),
        meta: {
          title: 'tnzi.admin.modules.workbench.title',
          permission: 'workbench.view',
          keepAlive: true,
          fixedIndexInTab: 0,
          order: 0,
        },
      },

      // ── User Center (self-service profile) ───────────────────
      // No permission needed — any authenticated user can manage their
      // own profile. Hidden from sidebar menus (not in module catalogue)
      // and reached via the avatar dropdown's "User Center" item.
      {
        path: 'user-center',
        name: 'user-center',
        component: () => import('../pages/account/UserCenter.vue'),
        meta: {
          title: 'tnzi.admin.modules.account.userCenter.title',
          hideInMenu: true,
        },
      },

      // ── Identity ──────────────────────────────────────────────
      {
        path: 'identity',
        name: 'identity',
        meta: { title: 'tnzi.admin.modules.identity.label', permission: 'identity.view', order: 100 },
        children: [
          {
            path: 'users',
            name: 'identity.users',
            component: () => import('../pages/identity/Users.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.users.title',
              permission: 'user.view',
              keepAlive: true,
            },
          },
          {
            path: 'roles',
            name: 'identity.roles',
            component: () => import('../pages/identity/Roles.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.roles.title',
              permission: 'role.view',
              keepAlive: true,
            },
          },
          {
            path: 'tenants',
            name: 'identity.tenants',
            component: () => import('../pages/identity/Tenants.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.tenants.title',
              permission: 'tenant.view',
              keepAlive: true,
            },
          },
          {
            path: 'login-logs',
            name: 'identity.loginLogs',
            component: () => import('../pages/identity/LoginLogs.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.loginLogs.title',
              permission: 'identity.loginLog.view',
              keepAlive: true,
            },
          },
          // GDPR admin route is intentionally NOT registered by default — the
          // backend has no admin-by-id GDPR endpoints yet (identity-bridge stubs
          // `gdpr.*` to reject with "not supported until admin endpoints land").
          // Consumers can re-add the route by registering it themselves once
          // the backend ships `DefaultGdprAdminController`.
          {
            path: 'organizations',
            name: 'identity.organizations',
            component: () => import('../pages/identity/Organizations.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.organizations.title',
              permission: 'organization.view',
              keepAlive: true,
            },
          },
          {
            path: 'sessions',
            name: 'identity.sessions',
            component: () => import('../pages/identity/Sessions.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.sessions.title',
              permission: 'session.view',
              keepAlive: true,
            },
          },
          {
            path: 'login-security',
            name: 'identity.loginSecurity',
            component: () => import('../pages/identity/LoginSecurity.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.loginSecurity.title',
              permission: 'identity.loginSecurity.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Authorization ─────────────────────────────────────────
      {
        path: 'authorization',
        name: 'authorization',
        meta: { title: 'tnzi.admin.modules.authorization.label', permission: 'authorization.view', order: 110 },
        children: [
          {
            path: 'function-modules',
            name: 'authorization.functionModules',
            component: () => import('../pages/authorization/FunctionModules.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.functionModules.title',
              permission: 'authorization.functionModule.view',
              keepAlive: true,
            },
          },
          {
            path: 'permissions',
            name: 'authorization.permissions',
            component: () => import('../pages/authorization/Permissions.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.permissions.title',
              permission: 'authorization.permission.view',
              keepAlive: true,
            },
          },
          {
            path: 'role-functions',
            name: 'authorization.roleFunctions',
            component: () => import('../pages/authorization/RoleFunctions.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.roleFunctions.title',
              permission: 'authorization.roleFunction.view',
              keepAlive: true,
            },
          },
          {
            path: 'entity-roles',
            name: 'authorization.entityRoles',
            component: () => import('../pages/authorization/EntityRoles.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.entityRoles.title',
              permission: 'authorization.entityRole.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── System ────────────────────────────────────────────────
      {
        path: 'system',
        name: 'system',
        meta: { title: 'tnzi.admin.modules.system.label', permission: 'system.view', order: 120 },
        children: [
          {
            path: 'menus',
            name: 'system.menus',
            component: () => import('../pages/system/Menus.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.menus.title',
              permission: 'system.menu.view',
              keepAlive: true,
            },
          },
          {
            path: 'dictionaries',
            name: 'system.dictionaries',
            component: () => import('../pages/system/Dictionaries.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.dictionaries.title',
              permission: 'system.dictionary.view',
              keepAlive: true,
            },
          },
          {
            path: 'access-logs',
            name: 'system.accessLogs',
            component: () => import('../pages/system/AccessLogs.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.accessLogs.title',
              permission: 'system.accessLog.view',
              keepAlive: true,
            },
          },
          {
            path: 'scheduled-jobs',
            name: 'system.scheduledJobs',
            component: () => import('../pages/system/ScheduledJobs.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.scheduledJobs.title',
              permission: 'system.scheduledJob.view',
              keepAlive: true,
            },
          },
          {
            path: 'features',
            name: 'system.features',
            component: () => import('../pages/system/Features.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.features.title',
              permission: 'feature.view',
              keepAlive: true,
            },
          },
          {
            path: 'log-files',
            name: 'system.logFiles',
            component: () => import('../pages/system/LogViewer.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.logFiles.title',
              permission: 'system.log.view',
              keepAlive: true,
            },
          },
          {
            path: 'diagnostics',
            name: 'system.diagnostics',
            component: () => import('../pages/system/Diagnostics.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.diagnostics.title',
              permission: 'system.diagnostics.view',
              keepAlive: true,
            },
          },
          {
            path: 'performance',
            name: 'system.performance',
            component: () => import('../pages/system/Performance.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.performance.title',
              permission: 'system.performance.view',
              keepAlive: true,
            },
          },
          {
            path: 'localization',
            name: 'system.localization',
            component: () => import('../pages/system/MissingTranslations.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.localization.title',
              permission: 'system.localization.view',
              keepAlive: true,
            },
          },
          {
            path: 'signalr',
            name: 'system.signalr',
            component: () => import('../pages/system/SignalRMonitor.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.signalr.title',
              permission: 'system.signalr.view',
              keepAlive: true,
            },
          },
          {
            path: 'health',
            name: 'system.health',
            component: () => import('../pages/system/HealthChecks.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.health.title',
              permission: 'system.health.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Settings Center ──────────────────────────────────────
      // Reached via the sidebar bottom icon (Task 14); not in the menu tree.
      {
        path: 'settings',
        name: 'settings',
        component: () => import('../pages/system/Settings.vue'),
        meta: {
          title: 'tnzi.admin.modules.system.settings.title',
          permission: 'system.parameter.view',
          hideInMenu: true,
        },
      },

      // ── Storage ───────────────────────────────────────────────
      {
        path: 'storage',
        name: 'storage',
        meta: { title: 'tnzi.admin.modules.storage.label', permission: 'storage.view', order: 160 },
        children: [
          {
            path: 'files',
            name: 'storage.files',
            component: () => import('../pages/storage/Files.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.files.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
          {
            path: 'chunks',
            name: 'storage.chunks',
            component: () => import('../pages/storage/Chunks.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.chunks.title',
              permission: 'storage.chunk.view',
              keepAlive: true,
            },
          },
          {
            path: 'versions',
            name: 'storage.versions',
            component: () => import('../pages/storage/Versions.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.versions.title',
              permission: 'storage.version.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Audit ─────────────────────────────────────────────────
      {
        path: 'audit',
        name: 'audit',
        meta: { title: 'tnzi.admin.modules.audit.label', permission: 'audit.view', order: 130 },
        children: [
          {
            path: 'logs',
            name: 'audit.logs',
            component: () => import('../pages/audit/Logs.vue'),
            meta: {
              title: 'tnzi.admin.modules.audit.logs.title',
              permission: 'audit.log.view',
              keepAlive: true,
            },
          },
          {
            path: 'operations',
            name: 'audit.operations',
            component: () => import('../pages/audit/Operations.vue'),
            meta: {
              title: 'tnzi.admin.modules.audit.operations.title',
              permission: 'audit.operation.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Notification ──────────────────────────────────────────
      {
        path: 'notification',
        name: 'notification',
        meta: { title: 'tnzi.admin.modules.notification.label', permission: 'notification.view', order: 170 },
        children: [
          {
            path: 'templates',
            name: 'notification.templates',
            component: () => import('../pages/notification/Templates.vue'),
            meta: {
              title: 'tnzi.admin.modules.notification.templates.title',
              permission: 'notification.template.view',
              keepAlive: true,
            },
          },
          {
            path: 'messages',
            name: 'notification.messages',
            component: () => import('../pages/notification/Messages.vue'),
            meta: {
              title: 'tnzi.admin.modules.notification.messages.title',
              permission: 'notification.message.view',
              keepAlive: true,
            },
          },
          {
            path: 'subscriptions',
            name: 'notification.subscriptions',
            component: () => import('../pages/notification/Subscriptions.vue'),
            meta: {
              title: 'tnzi.admin.modules.notification.subscriptions.title',
              permission: 'notification.subscription.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Chat ──────────────────────────────────────────────────
      {
        path: 'chat',
        name: 'chat',
        meta: { title: 'tnzi.admin.modules.chat.label', permission: 'chat.view', order: 140 },
        children: [
          {
            path: 'sessions',
            name: 'chat.sessions',
            component: () => import('../pages/chat/Sessions.vue'),
            meta: {
              title: 'tnzi.admin.modules.chat.sessions.title',
              permission: 'chat.session.view',
              keepAlive: true,
            },
          },
          {
            path: 'messages',
            name: 'chat.messages',
            component: () => import('../pages/chat/Messages.vue'),
            meta: {
              title: 'tnzi.admin.modules.chat.messages.title',
              permission: 'chat.message.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Payment ───────────────────────────────────────────────
      {
        path: 'payment',
        name: 'payment',
        meta: { title: 'tnzi.admin.modules.payment.label', permission: 'payment.view', order: 180 },
        children: [
          {
            path: 'orders',
            name: 'payment.orders',
            component: () => import('../pages/payment/Orders.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.orders.title',
              permission: 'payment.order.view',
              keepAlive: true,
            },
          },
          {
            path: 'subscriptions',
            name: 'payment.subscriptions',
            component: () => import('../pages/payment/Subscriptions.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.subscriptions.title',
              permission: 'payment.subscription.view',
              keepAlive: true,
            },
          },
          {
            path: 'refunds',
            name: 'payment.refunds',
            component: () => import('../pages/payment/Refunds.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.refunds.title',
              permission: 'payment.refund.view',
              keepAlive: true,
            },
          },
          {
            path: 'statistics',
            name: 'payment.statistics',
            component: () => import('../pages/payment/Statistics.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.statistics.title',
              permission: 'payment.statistics.view',
              keepAlive: true,
            },
          },
          {
            path: 'invoices',
            name: 'payment.invoices',
            component: () => import('../pages/payment/Invoices.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.invoices.title',
              permission: 'payment.invoice.view',
              keepAlive: true,
            },
          },
          {
            path: 'promotions',
            name: 'payment.promotions',
            component: () => import('../pages/payment/Promotions.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.promotions.title',
              permission: 'payment.promotion.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── AI ────────────────────────────────────────────────────
      // Menu order (array order = display order — children aren't
      //   sorted by meta.order in useAdminRouteStore.menus):
      //   1. Configuration  : agents / personas / skills / knowledge
      //   2. Execution      : workflows / workflowRuns / threads
      //   3. Infrastructure : providers / mcp
      //   4. Governance     : quota / usage / evaluations
      //
      // hideInMenu: true is applied to every dynamic-param child
      // (`/:id`, `/:agentId/runs/:runId?`) because they're entered from
      // their parent list page, never from the sidebar.
      {
        path: 'ai',
        name: 'ai',
        meta: { title: 'tnzi.admin.modules.ai.label', permission: 'ai.view', order: 150 },
        children: [
          // ── 1. Configuration ──
          {
            path: 'agents',
            name: 'ai.agents',
            component: () => import('../pages/ai/agents/Agents.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.agents.title',
              permission: 'ai.agent.view',
              keepAlive: true,
            },
          },
          {
            path: 'agents/:id',
            name: 'ai.agents.detail',
            component: () => import('../pages/ai/agents/AgentDetail.vue'),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.ai.agents.detail.title',
              permission: 'ai.agent.view',
              hideInMenu: true,
              activeMenu: 'ai.agents',
            },
          },
          {
            path: 'agents/:agentId/runs/:runId?',
            name: 'ai.agents.runs',
            component: () => import('../pages/ai/agents/AgentRunMonitor.vue'),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.ai.runMonitor.title',
              permission: 'ai.agentRun.view',
              hideInMenu: true,
              activeMenu: 'ai.agents',
            },
          },
          {
            path: 'personas',
            name: 'ai.personas',
            component: () => import('../pages/ai/personas/Personas.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.personas.title',
              permission: 'ai.persona.view',
              keepAlive: true,
            },
          },
          {
            path: 'skills',
            name: 'ai.skills',
            component: () => import('../pages/ai/skills/Skills.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.skills.title',
              permission: 'ai.skill.view',
              keepAlive: true,
            },
          },
          {
            path: 'knowledge',
            name: 'ai.knowledge',
            component: () => import('../pages/ai/knowledge/Knowledge.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.knowledge.title',
              permission: 'ai.knowledge.view',
              keepAlive: true,
            },
          },

          // ── 2. Execution ──
          {
            path: 'workflows',
            name: 'ai.workflows',
            component: () => import('../pages/ai/workflows/Workflows.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.workflows.title',
              permission: 'ai.workflow.view',
              keepAlive: true,
            },
          },
          {
            path: 'workflows/:id/edit',
            name: 'ai.workflows.editor',
            component: () => import('../pages/ai/workflows/WorkflowEditor.vue'),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.ai.workflows.editor.title',
              permission: 'ai.workflow.view',
              hideInMenu: true,
              activeMenu: 'ai.workflows',
            },
          },
          {
            path: 'workflow-runs',
            name: 'ai.workflowRuns',
            component: () => import('../pages/ai/workflows/WorkflowRunViewer.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.workflowRuns.title',
              permission: 'ai.workflowRun.view',
              keepAlive: true,
            },
          },
          {
            path: 'threads',
            name: 'ai.threads',
            component: () => import('../pages/ai/threads/Threads.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.threads.title',
              permission: 'ai.thread.view',
              keepAlive: true,
            },
          },

          // ── 3. Infrastructure ──
          {
            path: 'providers',
            name: 'ai.providers',
            component: () => import('../pages/ai/providers/Providers.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.providers.title',
              permission: 'ai.provider.view',
              keepAlive: true,
            },
          },
          {
            path: 'mcp',
            name: 'ai.mcp',
            component: () => import('../pages/ai/mcp/McpServers.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.mcp.title',
              permission: 'ai.mcp.view',
              keepAlive: true,
            },
          },
          {
            path: 'workspace-agents',
            name: 'ai.workspaceAgents',
            component: () => import('../pages/ai/workspace/WorkspaceAgents.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.workspaceAgents.title',
              permission: 'ai.agent.view',
              keepAlive: true,
            },
          },
          {
            path: 'channels',
            name: 'ai.channels',
            component: () => import('../pages/ai/channels/Channels.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.channels.title',
              permission: 'ai.channels.view',
              keepAlive: true,
            },
          },
          {
            path: 'sandbox',
            name: 'ai.sandbox',
            component: () => import('../pages/ai/sandbox/SandboxStatus.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.sandbox.title',
              permission: 'ai.sandbox.view',
              keepAlive: true,
            },
          },

          // ── 4. Governance ──
          {
            path: 'permissions',
            name: 'ai.permissions',
            component: () => import('../pages/ai/permissions/ToolPermissions.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.permissions.title',
              permission: 'ai.permissions.view',
              keepAlive: true,
            },
          },
          {
            path: 'quota',
            name: 'ai.quota',
            component: () => import('../pages/ai/quota/Quotas.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.quota.title',
              permission: 'ai.quota.view',
              keepAlive: true,
            },
          },
          {
            path: 'usage',
            name: 'ai.usage',
            component: () => import('../pages/ai/usage/UsageDashboard.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.usageDashboard.title',
              permission: 'ai.usage.view',
              keepAlive: true,
            },
          },
          {
            path: 'evaluations',
            name: 'ai.evaluations',
            component: () => import('../pages/ai/evaluations/EvaluationViewer.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.evaluations.title',
              permission: 'ai.evaluation.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Template ──────────────────────────────────────────────
      {
        path: 'template',
        name: 'template',
        meta: { title: 'tnzi.admin.modules.template.label', permission: 'template.view', order: 190 },
        children: [
          {
            path: 'templates',
            name: 'template.templates',
            component: () => import('../pages/template/Templates.vue'),
            meta: {
              title: 'tnzi.admin.modules.template.templates.title',
              permission: 'template.template.view',
              keepAlive: true,
            },
          },
          {
            path: 'layouts',
            name: 'template.layouts',
            component: () => import('../pages/template/Layouts.vue'),
            meta: {
              title: 'tnzi.admin.modules.template.layouts.title',
              permission: 'template.layout.view',
              keepAlive: true,
            },
          },
        ],
      },
    ],
  },
]
