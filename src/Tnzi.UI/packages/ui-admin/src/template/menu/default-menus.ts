import type { AdminMenuItem } from './types'

/**
 * Default admin menu tree.
 *
 * Critical invariants — violating these silently breaks navigation:
 *
 * 1. `key` MUST equal the route `name` in `router/routes.ts`. TAdminSidebar
 *    binds `<NMenu :value="route.name">` so the active highlight only lights
 *    up when keys match. Short keys like `'users'` against a route named
 *    `'identity.users'` render the menu but never highlight.
 *
 * 2. `path` MUST equal the full path resolved by vue-router (i.e. include
 *    the `/admin` prefix from `admin-root`). AdminShellRoot's onMenuSelect
 *    runs `router.push(menu.path)` directly without prepending anything.
 *
 * 3. Labels are i18n keys when prefixed with `admin.menu.`. TAdminSidebar's
 *    humanise fallback (Phase I.7.8) renders the last dot segment when the
 *    translation is missing, so a fresh consumer still sees readable text.
 *
 * Grouping rationale — business-domain first, not technical-module first:
 *   - dashboard → cold-landing page (workbench route)
 *   - iam → identity + authorization (one mental model: who-can-do-what)
 *   - ai → all AI surfaces (matches the 13 pages under routes.ts ai/*)
 *   - chat → message inspection (kept separate from ai because operators
 *            triage chat threads on a different cadence than agent ops)
 *   - content → user-facing assets (template + storage + notification)
 *   - commerce → payment (renamed; "payment" is too narrow once we add
 *                pricing/coupons/etc.)
 *   - system → operator-facing platform config
 *   - audit → read-only audit trail (kept last because it's accessed
 *             during incidents, not in day-to-day flow)
 */
export const defaultAdminMenus: AdminMenuItem[] = [
  {
    key: 'workbench',
    label: 'admin.menu.workbench',
    icon: 'LayoutDashboard',
    path: '/admin/workbench',
  },
  {
    key: 'iam',
    label: 'admin.menu.iam._label',
    icon: 'ShieldCheck',
    children: [
      { key: 'identity.users', label: 'admin.menu.identity.users', path: '/admin/identity/users', permission: 'user.view' },
      { key: 'identity.roles', label: 'admin.menu.identity.roles', path: '/admin/identity/roles', permission: 'role.view' },
      { key: 'identity.tenants', label: 'admin.menu.identity.tenants', path: '/admin/identity/tenants', permission: 'tenant.view' },
      { key: 'identity.organizations', label: 'admin.menu.identity.organizations', path: '/admin/identity/organizations', permission: 'organization.view' },
      { key: 'identity.sessions', label: 'admin.menu.identity.sessions', path: '/admin/identity/sessions', permission: 'session.view' },
      { key: 'identity.loginLogs', label: 'admin.menu.identity.loginLogs', path: '/admin/identity/login-logs', permission: 'identity.loginLog.view' },
      { key: 'identity.gdpr', label: 'admin.menu.identity.gdpr', path: '/admin/identity/gdpr', permission: 'identity.gdpr.view' },
      { key: 'authorization.functionModules', label: 'admin.menu.authorization.functionModules', path: '/admin/authorization/function-modules', permission: 'authorization.functionModule.view' },
      { key: 'authorization.roleFunctions', label: 'admin.menu.authorization.roleFunctions', path: '/admin/authorization/role-functions', permission: 'authorization.roleFunction.view' },
      { key: 'authorization.entityRoles', label: 'admin.menu.authorization.entityRoles', path: '/admin/authorization/entity-roles', permission: 'authorization.entityRole.view' },
      { key: 'authorization.permissions', label: 'admin.menu.authorization.permissions', path: '/admin/authorization/permissions', permission: 'authorization.permission.view' },
    ],
  },
  {
    key: 'ai',
    label: 'admin.menu.ai._label',
    icon: 'Sparkles',
    children: [
      { key: 'ai.agents', label: 'admin.menu.ai.agents', path: '/admin/ai/agents', permission: 'ai.agent.view' },
      { key: 'ai.workflows', label: 'admin.menu.ai.workflows', path: '/admin/ai/workflows', permission: 'ai.workflow.view' },
      { key: 'ai.workflowRuns', label: 'admin.menu.ai.workflowRuns', path: '/admin/ai/workflow-runs', permission: 'ai.workflowRun.view' },
      { key: 'ai.skills', label: 'admin.menu.ai.skills', path: '/admin/ai/skills', permission: 'ai.skill.view' },
      { key: 'ai.personas', label: 'admin.menu.ai.personas', path: '/admin/ai/personas', permission: 'ai.persona.view' },
      { key: 'ai.knowledge', label: 'admin.menu.ai.knowledge', path: '/admin/ai/knowledge', permission: 'ai.knowledge.view' },
      { key: 'ai.mcp', label: 'admin.menu.ai.mcp', path: '/admin/ai/mcp', permission: 'ai.mcp.view' },
      { key: 'ai.providers', label: 'admin.menu.ai.providers', path: '/admin/ai/providers', permission: 'ai.provider.view' },
      { key: 'ai.quota', label: 'admin.menu.ai.quota', path: '/admin/ai/quota', permission: 'ai.quota.view' },
      { key: 'ai.threads', label: 'admin.menu.ai.threads', path: '/admin/ai/threads', permission: 'ai.thread.view' },
      { key: 'ai.usage', label: 'admin.menu.ai.usage', path: '/admin/ai/usage', permission: 'ai.usage.view' },
      { key: 'ai.evaluations', label: 'admin.menu.ai.evaluations', path: '/admin/ai/evaluations', permission: 'ai.evaluation.view' },
    ],
  },
  {
    key: 'chat',
    label: 'admin.menu.chat._label',
    icon: 'MessageSquare',
    children: [
      { key: 'chat.sessions', label: 'admin.menu.chat.sessions', path: '/admin/chat/sessions', permission: 'chat.session.view' },
      { key: 'chat.messages', label: 'admin.menu.chat.messages', path: '/admin/chat/messages', permission: 'chat.message.view' },
    ],
  },
  {
    key: 'content',
    label: 'admin.menu.content',
    icon: 'FolderOpen',
    children: [
      { key: 'template.templates', label: 'admin.menu.template.templates', path: '/admin/template/templates', permission: 'template.template.view' },
      { key: 'template.layouts', label: 'admin.menu.template.layouts', path: '/admin/template/layouts', permission: 'template.layout.view' },
      { key: 'storage.files', label: 'admin.menu.storage.files', path: '/admin/storage/files', permission: 'storage.file.view' },
      { key: 'storage.chunks', label: 'admin.menu.storage.chunks', path: '/admin/storage/chunks', permission: 'storage.chunk.view' },
      { key: 'storage.versions', label: 'admin.menu.storage.versions', path: '/admin/storage/versions', permission: 'storage.version.view' },
      { key: 'notification.templates', label: 'admin.menu.notification.templates', path: '/admin/notification/templates', permission: 'notification.template.view' },
      { key: 'notification.messages', label: 'admin.menu.notification.messages', path: '/admin/notification/messages', permission: 'notification.message.view' },
      { key: 'notification.subscriptions', label: 'admin.menu.notification.subscriptions', path: '/admin/notification/subscriptions', permission: 'notification.subscription.view' },
    ],
  },
  {
    key: 'commerce',
    label: 'admin.menu.commerce',
    icon: 'CreditCard',
    children: [
      { key: 'payment.orders', label: 'admin.menu.payment.orders', path: '/admin/payment/orders', permission: 'payment.order.view' },
      { key: 'payment.subscriptions', label: 'admin.menu.payment.subscriptions', path: '/admin/payment/subscriptions', permission: 'payment.subscription.view' },
      { key: 'payment.refunds', label: 'admin.menu.payment.refunds', path: '/admin/payment/refunds', permission: 'payment.refund.view' },
    ],
  },
  {
    key: 'system',
    label: 'admin.menu.system._label',
    icon: 'Settings',
    children: [
      { key: 'system.menus', label: 'admin.menu.system.menus', path: '/admin/system/menus', permission: 'system.menu.view' },
      { key: 'system.dictionaries', label: 'admin.menu.system.dictionaries', path: '/admin/system/dictionaries', permission: 'system.dictionary.view' },
      { key: 'system.parameters', label: 'admin.menu.system.parameters', path: '/admin/system/parameters', permission: 'system.parameter.view' },
      { key: 'system.scheduledJobs', label: 'admin.menu.system.scheduledJobs', path: '/admin/system/scheduled-jobs', permission: 'system.scheduledJob.view' },
      { key: 'system.features', label: 'admin.menu.system.features', path: '/admin/system/features', permission: 'feature.view' },
      { key: 'system.accessLogs', label: 'admin.menu.system.accessLogs', path: '/admin/system/access-logs', permission: 'system.accessLog.view' },
    ],
  },
  {
    key: 'audit',
    label: 'admin.menu.audit._label',
    icon: 'ClipboardList',
    children: [
      { key: 'audit.logs', label: 'admin.menu.audit.logs', path: '/admin/audit/logs', permission: 'audit.log.view' },
      { key: 'audit.operations', label: 'admin.menu.audit.operations', path: '/admin/audit/operations', permission: 'audit.operation.view' },
    ],
  },
]
