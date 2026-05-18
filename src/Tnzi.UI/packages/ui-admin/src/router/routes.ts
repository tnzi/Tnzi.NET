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
      {
        path: 'workbench',
        name: 'workbench',
        component: () => import('../pages/dashboard/Workbench.vue'),
        meta: {
          title: 'tnzi.admin.modules.workbench.title',
          permission: 'workbench.view',
          keepAlive: true,
          fixedIndexInTab: 0,
        },
      },

      // ── Identity ──────────────────────────────────────────────
      {
        path: 'identity',
        name: 'identity',
        meta: { title: 'tnzi.admin.modules.identity.label', permission: 'identity.view' },
        children: [
          {
            path: 'users',
            name: 'identity.users',
            component: () => import('../pages/identity/UserManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.users.title',
              permission: 'user.view',
              keepAlive: true,
            },
          },
          {
            path: 'roles',
            name: 'identity.roles',
            component: () => import('../pages/identity/RoleManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.roles.title',
              permission: 'role.view',
              keepAlive: true,
            },
          },
          {
            path: 'tenants',
            name: 'identity.tenants',
            component: () => import('../pages/identity/TenantManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.tenants.title',
              permission: 'tenant.view',
              keepAlive: true,
            },
          },
          {
            path: 'login-logs',
            name: 'identity.loginLogs',
            component: () => import('../pages/identity/LoginLog.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.loginLogs.title',
              permission: 'identity.loginLog.view',
              keepAlive: true,
            },
          },
          {
            path: 'gdpr',
            name: 'identity.gdpr',
            component: () => import('../pages/identity/GdprRequests.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.gdpr.title',
              permission: 'identity.gdpr.view',
              keepAlive: true,
            },
          },
          {
            path: 'organizations',
            name: 'identity.organizations',
            component: () => import('../pages/identity/OrganizationManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.organizations.title',
              permission: 'organization.view',
              keepAlive: true,
            },
          },
          {
            path: 'sessions',
            name: 'identity.sessions',
            component: () => import('../pages/identity/SessionManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.identity.sessions.title',
              permission: 'session.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Authorization ─────────────────────────────────────────
      {
        path: 'authorization',
        name: 'authorization',
        meta: { title: 'tnzi.admin.modules.authorization.label', permission: 'authorization.view' },
        children: [
          {
            path: 'function-modules',
            name: 'authorization.functionModules',
            component: () => import('../pages/authorization/FunctionModule.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.functionModules.title',
              permission: 'authorization.functionModule.view',
              keepAlive: true,
            },
          },
          {
            path: 'permissions',
            name: 'authorization.permissions',
            component: () => import('../pages/authorization/Permission.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.permissions.title',
              permission: 'authorization.permission.view',
              keepAlive: true,
            },
          },
          {
            path: 'role-functions',
            name: 'authorization.roleFunctions',
            component: () => import('../pages/authorization/RoleFunction.vue'),
            meta: {
              title: 'tnzi.admin.modules.authorization.roleFunctions.title',
              permission: 'authorization.roleFunction.view',
              keepAlive: true,
            },
          },
          {
            path: 'entity-roles',
            name: 'authorization.entityRoles',
            component: () => import('../pages/authorization/EntityRole.vue'),
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
        meta: { title: 'tnzi.admin.modules.system.label', permission: 'system.view' },
        children: [
          {
            path: 'menus',
            name: 'system.menus',
            component: () => import('../pages/system/MenuManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.menus.title',
              permission: 'system.menu.view',
              keepAlive: true,
            },
          },
          {
            path: 'dictionaries',
            name: 'system.dictionaries',
            component: () => import('../pages/system/DictionaryManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.dictionaries.title',
              permission: 'system.dictionary.view',
              keepAlive: true,
            },
          },
          {
            path: 'parameters',
            name: 'system.parameters',
            component: () => import('../pages/system/ParameterManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.parameters.title',
              permission: 'system.parameter.view',
              keepAlive: true,
            },
          },
          {
            path: 'access-logs',
            name: 'system.accessLogs',
            component: () => import('../pages/system/AccessLog.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.accessLogs.title',
              permission: 'system.accessLog.view',
              keepAlive: true,
            },
          },
          {
            path: 'scheduled-jobs',
            name: 'system.scheduledJobs',
            component: () => import('../pages/system/ScheduledJob.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.scheduledJobs.title',
              permission: 'system.scheduledJob.view',
              keepAlive: true,
            },
          },
          {
            path: 'features',
            name: 'system.features',
            component: () => import('../pages/system/FeatureManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.features.title',
              permission: 'feature.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Storage ───────────────────────────────────────────────
      {
        path: 'storage',
        name: 'storage',
        meta: { title: 'tnzi.admin.modules.storage.label', permission: 'storage.view' },
        children: [
          {
            path: 'files',
            name: 'storage.files',
            component: () => import('../pages/storage/StorageFile.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.files.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
          {
            path: 'chunks',
            name: 'storage.chunks',
            component: () => import('../pages/storage/StorageChunk.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.chunks.title',
              permission: 'storage.chunk.view',
              keepAlive: true,
            },
          },
          {
            path: 'versions',
            name: 'storage.versions',
            component: () => import('../pages/storage/StorageVersion.vue'),
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
        meta: { title: 'tnzi.admin.modules.audit.label', permission: 'audit.view' },
        children: [
          {
            path: 'logs',
            name: 'audit.logs',
            component: () => import('../pages/audit/AuditLog.vue'),
            meta: {
              title: 'tnzi.admin.modules.audit.logs.title',
              permission: 'audit.log.view',
              keepAlive: true,
            },
          },
          {
            path: 'operations',
            name: 'audit.operations',
            component: () => import('../pages/audit/AuditOperation.vue'),
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
        meta: { title: 'tnzi.admin.modules.notification.label', permission: 'notification.view' },
        children: [
          {
            path: 'templates',
            name: 'notification.templates',
            component: () => import('../pages/notification/NotificationTemplate.vue'),
            meta: {
              title: 'tnzi.admin.modules.notification.templates.title',
              permission: 'notification.template.view',
              keepAlive: true,
            },
          },
          {
            path: 'messages',
            name: 'notification.messages',
            component: () => import('../pages/notification/NotificationMessage.vue'),
            meta: {
              title: 'tnzi.admin.modules.notification.messages.title',
              permission: 'notification.message.view',
              keepAlive: true,
            },
          },
          {
            path: 'subscriptions',
            name: 'notification.subscriptions',
            component: () => import('../pages/notification/NotificationSubscription.vue'),
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
        meta: { title: 'tnzi.admin.modules.chat.label', permission: 'chat.view' },
        children: [
          {
            path: 'sessions',
            name: 'chat.sessions',
            component: () => import('../pages/chat/ChatSession.vue'),
            meta: {
              title: 'tnzi.admin.modules.chat.sessions.title',
              permission: 'chat.session.view',
              keepAlive: true,
            },
          },
          {
            path: 'messages',
            name: 'chat.messages',
            component: () => import('../pages/chat/ChatMessage.vue'),
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
        meta: { title: 'tnzi.admin.modules.payment.label', permission: 'payment.view' },
        children: [
          {
            path: 'orders',
            name: 'payment.orders',
            component: () => import('../pages/payment/PaymentOrder.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.orders.title',
              permission: 'payment.order.view',
              keepAlive: true,
            },
          },
          {
            path: 'subscriptions',
            name: 'payment.subscriptions',
            component: () => import('../pages/payment/PaymentSubscription.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.subscriptions.title',
              permission: 'payment.subscription.view',
              keepAlive: true,
            },
          },
          {
            path: 'refunds',
            name: 'payment.refunds',
            component: () => import('../pages/payment/PaymentRefund.vue'),
            meta: {
              title: 'tnzi.admin.modules.payment.refunds.title',
              permission: 'payment.refund.view',
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
        meta: { title: 'tnzi.admin.modules.ai.label', permission: 'ai.view' },
        children: [
          // ── 1. Configuration ──
          {
            path: 'agents',
            name: 'ai.agents',
            component: () => import('../pages/ai/agents/AgentList.vue'),
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
            component: () => import('../pages/ai/agents/RunMonitor.vue'),
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
            component: () => import('../pages/ai/personas/PersonaList.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.personas.title',
              permission: 'ai.persona.view',
              keepAlive: true,
            },
          },
          {
            path: 'skills',
            name: 'ai.skills',
            component: () => import('../pages/ai/skills/SkillList.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.skills.title',
              permission: 'ai.skill.view',
              keepAlive: true,
            },
          },
          {
            path: 'knowledge',
            name: 'ai.knowledge',
            component: () => import('../pages/ai/knowledge/KbManager.vue'),
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
            component: () => import('../pages/ai/workflows/WorkflowEditor.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.workflows.title',
              permission: 'ai.workflow.view',
              keepAlive: true,
            },
          },
          {
            path: 'workflows/:id',
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
            component: () => import('../pages/ai/workflows/RunViewer.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.workflowRuns.title',
              permission: 'ai.workflowRun.view',
              keepAlive: true,
            },
          },
          {
            path: 'threads',
            name: 'ai.threads',
            component: () => import('../pages/ai/threads/ThreadList.vue'),
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
            component: () => import('../pages/ai/providers/ProviderConfig.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.providers.title',
              permission: 'ai.provider.view',
              keepAlive: true,
            },
          },
          {
            path: 'mcp',
            name: 'ai.mcp',
            component: () => import('../pages/ai/mcp/McpServerList.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.mcp.title',
              permission: 'ai.mcp.view',
              keepAlive: true,
            },
          },

          // ── 4. Governance ──
          {
            path: 'quota',
            name: 'ai.quota',
            component: () => import('../pages/ai/quota/QuotaRules.vue'),
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
            component: () => import('../pages/ai/evaluations/EvalViewer.vue'),
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
        meta: { title: 'tnzi.admin.modules.template.label', permission: 'template.view' },
        children: [
          {
            path: 'templates',
            name: 'template.templates',
            component: () => import('../pages/template/TemplateManagement.vue'),
            meta: {
              title: 'tnzi.admin.modules.template.templates.title',
              permission: 'template.template.view',
              keepAlive: true,
            },
          },
          {
            path: 'layouts',
            name: 'template.layouts',
            component: () => import('../pages/template/TemplateLayout.vue'),
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
