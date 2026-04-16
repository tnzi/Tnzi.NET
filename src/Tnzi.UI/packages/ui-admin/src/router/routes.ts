import { defineAsyncComponent, defineComponent, h } from 'vue'
import type { RouteRecordRaw } from 'vue-router'

/**
 * Default admin routes — Phase 3.38: all 28 module pages registered.
 *
 * Placeholder components are kept for login/forbidden.
 * All admin pages use defineAsyncComponent for code splitting.
 */

const LoginPlaceholder = defineComponent({
  name: 'LoginPlaceholder',
  render: () => h('div', 'Login (override me)'),
})

const ForbiddenPlaceholder = defineComponent({
  name: 'ForbiddenPlaceholder',
  render: () => h('div', '403 Forbidden'),
})

export const defaultAdminRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: LoginPlaceholder,
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
    redirect: '/admin/identity/users',
    meta: { requiresAuth: true, title: 'Admin' },
    children: [
      // ── Identity ──────────────────────────────────────────────
      {
        path: 'identity',
        name: 'identity',
        meta: { title: 'Identity', permission: 'identity.view' },
        children: [
          {
            path: 'users',
            name: 'identity.users',
            component: defineAsyncComponent(() => import('../pages/identity/UserManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.identity.users.title',
              permission: 'user.view',
              keepAlive: true,
            },
          },
          {
            path: 'roles',
            name: 'identity.roles',
            component: defineAsyncComponent(() => import('../pages/identity/RoleManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.identity.roles.title',
              permission: 'role.view',
              keepAlive: true,
            },
          },
          {
            path: 'tenants',
            name: 'identity.tenants',
            component: defineAsyncComponent(() => import('../pages/identity/TenantManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.identity.tenants.title',
              permission: 'tenant.view',
              keepAlive: true,
            },
          },
          {
            path: 'login-logs',
            name: 'identity.loginLogs',
            component: defineAsyncComponent(() => import('../pages/identity/LoginLog.vue')),
            meta: {
              title: 'tnzi.admin.modules.identity.loginLogs.title',
              permission: 'identity.loginLog.view',
              keepAlive: true,
            },
          },
          {
            path: 'gdpr',
            name: 'identity.gdpr',
            component: defineAsyncComponent(() => import('../pages/identity/GdprRequests.vue')),
            meta: {
              title: 'tnzi.admin.modules.identity.gdpr.title',
              permission: 'identity.gdpr.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Authorization ─────────────────────────────────────────
      {
        path: 'authorization',
        name: 'authorization',
        meta: { title: 'Authorization', permission: 'authorization.view' },
        children: [
          {
            path: 'function-modules',
            name: 'authorization.functionModules',
            component: defineAsyncComponent(() => import('../pages/authorization/FunctionModule.vue')),
            meta: {
              title: 'tnzi.admin.modules.authorization.functionModules.title',
              permission: 'authorization.functionModule.view',
              keepAlive: true,
            },
          },
          {
            path: 'permissions',
            name: 'authorization.permissions',
            component: defineAsyncComponent(() => import('../pages/authorization/Permission.vue')),
            meta: {
              title: 'tnzi.admin.modules.authorization.permissions.title',
              permission: 'authorization.permission.view',
              keepAlive: true,
            },
          },
          {
            path: 'role-functions',
            name: 'authorization.roleFunctions',
            component: defineAsyncComponent(() => import('../pages/authorization/RoleFunction.vue')),
            meta: {
              title: 'tnzi.admin.modules.authorization.roleFunctions.title',
              permission: 'authorization.roleFunction.view',
              keepAlive: true,
            },
          },
          {
            path: 'entity-roles',
            name: 'authorization.entityRoles',
            component: defineAsyncComponent(() => import('../pages/authorization/EntityRole.vue')),
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
        meta: { title: 'System', permission: 'system.view' },
        children: [
          {
            path: 'menus',
            name: 'system.menus',
            component: defineAsyncComponent(() => import('../pages/system/MenuManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.system.menus.title',
              permission: 'system.menu.view',
              keepAlive: true,
            },
          },
          {
            path: 'dictionaries',
            name: 'system.dictionaries',
            component: defineAsyncComponent(() => import('../pages/system/DictionaryManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.system.dictionaries.title',
              permission: 'system.dictionary.view',
              keepAlive: true,
            },
          },
          {
            path: 'parameters',
            name: 'system.parameters',
            component: defineAsyncComponent(() => import('../pages/system/ParameterManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.system.parameters.title',
              permission: 'system.parameter.view',
              keepAlive: true,
            },
          },
          {
            path: 'access-logs',
            name: 'system.accessLogs',
            component: defineAsyncComponent(() => import('../pages/system/AccessLog.vue')),
            meta: {
              title: 'tnzi.admin.modules.system.accessLogs.title',
              permission: 'system.accessLog.view',
              keepAlive: true,
            },
          },
          {
            path: 'scheduled-jobs',
            name: 'system.scheduledJobs',
            component: defineAsyncComponent(() => import('../pages/system/ScheduledJob.vue')),
            meta: {
              title: 'tnzi.admin.modules.system.scheduledJobs.title',
              permission: 'system.scheduledJob.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Storage ───────────────────────────────────────────────
      {
        path: 'storage',
        name: 'storage',
        meta: { title: 'Storage', permission: 'storage.view' },
        children: [
          {
            path: 'files',
            name: 'storage.files',
            component: defineAsyncComponent(() => import('../pages/storage/StorageFile.vue')),
            meta: {
              title: 'tnzi.admin.modules.storage.files.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
          {
            path: 'chunks',
            name: 'storage.chunks',
            component: defineAsyncComponent(() => import('../pages/storage/StorageChunk.vue')),
            meta: {
              title: 'tnzi.admin.modules.storage.chunks.title',
              permission: 'storage.chunk.view',
              keepAlive: true,
            },
          },
          {
            path: 'versions',
            name: 'storage.versions',
            component: defineAsyncComponent(() => import('../pages/storage/StorageVersion.vue')),
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
        meta: { title: 'Audit', permission: 'audit.view' },
        children: [
          {
            path: 'logs',
            name: 'audit.logs',
            component: defineAsyncComponent(() => import('../pages/audit/AuditLog.vue')),
            meta: {
              title: 'tnzi.admin.modules.audit.logs.title',
              permission: 'audit.log.view',
              keepAlive: true,
            },
          },
          {
            path: 'operations',
            name: 'audit.operations',
            component: defineAsyncComponent(() => import('../pages/audit/AuditOperation.vue')),
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
        meta: { title: 'Notification', permission: 'notification.view' },
        children: [
          {
            path: 'templates',
            name: 'notification.templates',
            component: defineAsyncComponent(() => import('../pages/notification/NotificationTemplate.vue')),
            meta: {
              title: 'tnzi.admin.modules.notification.templates.title',
              permission: 'notification.template.view',
              keepAlive: true,
            },
          },
          {
            path: 'messages',
            name: 'notification.messages',
            component: defineAsyncComponent(() => import('../pages/notification/NotificationMessage.vue')),
            meta: {
              title: 'tnzi.admin.modules.notification.messages.title',
              permission: 'notification.message.view',
              keepAlive: true,
            },
          },
          {
            path: 'subscriptions',
            name: 'notification.subscriptions',
            component: defineAsyncComponent(() => import('../pages/notification/NotificationSubscription.vue')),
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
        meta: { title: 'Chat', permission: 'chat.view' },
        children: [
          {
            path: 'sessions',
            name: 'chat.sessions',
            component: defineAsyncComponent(() => import('../pages/chat/ChatSession.vue')),
            meta: {
              title: 'tnzi.admin.modules.chat.sessions.title',
              permission: 'chat.session.view',
              keepAlive: true,
            },
          },
          {
            path: 'messages',
            name: 'chat.messages',
            component: defineAsyncComponent(() => import('../pages/chat/ChatMessage.vue')),
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
        meta: { title: 'Payment', permission: 'payment.view' },
        children: [
          {
            path: 'orders',
            name: 'payment.orders',
            component: defineAsyncComponent(() => import('../pages/payment/PaymentOrder.vue')),
            meta: {
              title: 'tnzi.admin.modules.payment.orders.title',
              permission: 'payment.order.view',
              keepAlive: true,
            },
          },
          {
            path: 'subscriptions',
            name: 'payment.subscriptions',
            component: defineAsyncComponent(() => import('../pages/payment/PaymentSubscription.vue')),
            meta: {
              title: 'tnzi.admin.modules.payment.subscriptions.title',
              permission: 'payment.subscription.view',
              keepAlive: true,
            },
          },
          {
            path: 'refunds',
            name: 'payment.refunds',
            component: defineAsyncComponent(() => import('../pages/payment/PaymentRefund.vue')),
            meta: {
              title: 'tnzi.admin.modules.payment.refunds.title',
              permission: 'payment.refund.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── AI ────────────────────────────────────────────────────
      {
        path: 'ai',
        name: 'ai',
        meta: { title: 'tnzi.admin.modules.ai.label', permission: 'ai.view' },
        children: [
          {
            path: 'agents',
            name: 'ai.agents',
            component: defineAsyncComponent(() => import('../pages/ai/agents/AgentList.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.agents.title',
              permission: 'ai.agent.view',
              keepAlive: true,
            },
          },
          {
            path: 'agents/:id',
            name: 'ai.agents.detail',
            component: defineAsyncComponent(() => import('../pages/ai/agents/AgentDetail.vue')),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.ai.agents.detail.title',
              permission: 'ai.agent.view',
            },
          },
          {
            path: 'agents/:agentId/runs/:runId?',
            name: 'ai.agents.runs',
            component: defineAsyncComponent(() => import('../pages/ai/agents/RunMonitor.vue')),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.ai.runMonitor.title',
              permission: 'ai.agentRun.view',
            },
          },
          {
            path: 'workflows',
            name: 'ai.workflows',
            component: defineAsyncComponent(() => import('../pages/ai/workflows/WorkflowEditor.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.workflows.title',
              permission: 'ai.workflow.view',
              keepAlive: true,
            },
          },
          {
            path: 'workflows/:id',
            name: 'ai.workflows.editor',
            component: defineAsyncComponent(() => import('../pages/ai/workflows/WorkflowEditor.vue')),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.ai.workflows.editor.title',
              permission: 'ai.workflow.view',
            },
          },
          {
            path: 'workflow-runs',
            name: 'ai.workflowRuns',
            component: defineAsyncComponent(() => import('../pages/ai/workflows/RunViewer.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.workflowRuns.title',
              permission: 'ai.workflowRun.view',
              keepAlive: true,
            },
          },
          {
            path: 'skills',
            name: 'ai.skills',
            component: defineAsyncComponent(() => import('../pages/ai/skills/SkillList.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.skills.title',
              permission: 'ai.skill.view',
              keepAlive: true,
            },
          },
          {
            path: 'providers',
            name: 'ai.providers',
            component: defineAsyncComponent(() => import('../pages/ai/providers/ProviderConfig.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.providers.title',
              permission: 'ai.provider.view',
              keepAlive: true,
            },
          },
          {
            path: 'usage',
            name: 'ai.usage',
            component: defineAsyncComponent(() => import('../pages/ai/usage/UsageDashboard.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.usageDashboard.title',
              permission: 'ai.usage.view',
              keepAlive: true,
            },
          },
          {
            path: 'knowledge',
            name: 'ai.knowledge',
            component: defineAsyncComponent(() => import('../pages/ai/knowledge/KbManager.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.knowledge.title',
              permission: 'ai.knowledge.view',
              keepAlive: true,
            },
          },
          {
            path: 'mcp',
            name: 'ai.mcp',
            component: defineAsyncComponent(() => import('../pages/ai/mcp/McpServerList.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.mcp.title',
              permission: 'ai.mcp.view',
              keepAlive: true,
            },
          },
          {
            path: 'quota',
            name: 'ai.quota',
            component: defineAsyncComponent(() => import('../pages/ai/quota/QuotaRules.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.quota.title',
              permission: 'ai.quota.view',
              keepAlive: true,
            },
          },
          {
            path: 'personas',
            name: 'ai.personas',
            component: defineAsyncComponent(() => import('../pages/ai/personas/PersonaList.vue')),
            meta: {
              title: 'tnzi.admin.modules.ai.personas.title',
              permission: 'ai.persona.view',
              keepAlive: true,
            },
          },
          {
            path: 'evaluations',
            name: 'ai.evaluations',
            component: defineAsyncComponent(() => import('../pages/ai/evaluations/EvalViewer.vue')),
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
        meta: { title: 'Template', permission: 'template.view' },
        children: [
          {
            path: 'templates',
            name: 'template.templates',
            component: defineAsyncComponent(() => import('../pages/template/TemplateManagement.vue')),
            meta: {
              title: 'tnzi.admin.modules.template.templates.title',
              permission: 'template.template.view',
              keepAlive: true,
            },
          },
          {
            path: 'layouts',
            name: 'template.layouts',
            component: defineAsyncComponent(() => import('../pages/template/TemplateLayout.vue')),
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
