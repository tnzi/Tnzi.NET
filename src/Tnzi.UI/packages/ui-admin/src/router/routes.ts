import type { RouteRecordRaw } from 'vue-router'
import AdminShellRoot from './AdminShellRoot.vue'

/**
 * Default admin routes - Phase 3.38: all 28 module pages registered.
 *
 * A placeholder component is kept for login only. The 403 / 404 / 500 routes
 * render the soybean-styled {@link ExceptionView} (a router + i18n wrapper
 * around `TExceptionPage`) instead of the old bare `h('div', '403 Forbidden')`.
 * All admin pages use vue-router's async component syntax (`() => import(...)`)
 * for code splitting - vue-router handles the async loading + internal Suspense
 * boundary itself; wrapping in `defineAsyncComponent` triggers a warning and
 * conflicts with router-level Suspense (see Phase H3 regression).
 * The `/admin` parent renders {@link AdminShellRoot} so every child page
 * inherits the standard TAdminShell layout (sidebar + header + content).
 *
 * ── Module-availability gating (`meta.moduleGate`) ──
 * Every top-level FRAMEWORK module node below carries `meta.moduleGate: true`.
 * When `defineAdminApp({ moduleGating })` is on (the default), the shell fetches
 * `GET /admin/shell/modules` (which framework `TnziApplicationModule`s the host
 * actually loaded) and HIDES + makes UNREACHABLE any gated node whose module the
 * backend didn't load - so an unloaded module (Finance / Payment / AI …) never
 * surfaces a dead menu that 404s on click. This is orthogonal to permissions, so
 * it holds for super-admins too. `moduleGate: true` uses the node's own `name`
 * as the module key; a consumer module registered via `addModules` can opt in
 * with `moduleGate: '<its-TnziApplicationModule-short-name>'`. A standalone
 * page whose backend lives in one module can gate with an explicit name (the
 * Settings Center carries `moduleGate: 'system'`). Nodes WITHOUT `moduleGate`
 * (dashboard / user-center, consumer pages) are never gated. When the signal
 * is unavailable the shell shows everything (fail-open).
 */

// Single wired exception page (403 / 404 / 500) - reads `meta.exceptionType`.
// Lazy so the exception bundle isn't pulled into the initial chunk.
const ExceptionView = () => import('../pages/exception/ExceptionView.vue')

export const defaultAdminRoutes: RouteRecordRaw[] = [
  {
    // Soybean parity: `/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat)?`.
    // The module param defaults to `pwd-login` inside the page component; the
    // regex constraint rejects unknown values and routes them to a 404. Order
    // matters - the path-param route must come before the bare `/login` redirect.
    path: '/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat|two-factor)?',
    name: 'login',
    component: () => import('../pages/login/LoginView.vue'),
    meta: { requiresAuth: false, title: 'Login' },
  },
  {
    // 分享链接的收件人页面。`requiresAuth: false` 是关键 —— 收件人是客户 / 审计师 /
    // 供应商，他们没有账号，这正是对外分享的定义。页面自绘一张居中卡片，不挂 admin 外壳。
    path: '/share/:token',
    name: 'share-link',
    component: () => import('../pages/share/SharePage.vue'),
    meta: { requiresAuth: false, title: 'Shared file' },
  },
  // Exception pages - top-level (so `applyBasePath` prefixes them uniformly with
  // every other route) and `requiresAuth: false` so the permission / module
  // guards can redirect INTO them without the auth guard bouncing back. Each
  // carries `meta.exceptionType`, which ExceptionView reads to pick the preset.
  {
    path: '/403',
    name: 'forbidden',
    component: ExceptionView,
    meta: { requiresAuth: false, title: '403', exceptionType: '403' },
  },
  {
    path: '/404',
    name: 'not-found',
    component: ExceptionView,
    meta: { requiresAuth: false, title: '404', exceptionType: '404' },
  },
  {
    path: '/500',
    name: 'server-error',
    component: ExceptionView,
    meta: { requiresAuth: false, title: '500', exceptionType: '500' },
  },
  {
    path: '/admin',
    name: 'admin-root',
    component: AdminShellRoot,
    // Redirect by NAME, not path: `defineAdminApp({ basePath })` rewrites the
    // top-level paths, so a literal '/admin/dashboard' would dangle under any
    // custom prefix. Name resolution always lands on the rewritten route.
    redirect: { name: 'dashboard' },
    meta: { requiresAuth: true, title: 'Admin' },
    children: [
      // ── Dashboard (default landing page) ─────────────────────
      // Default `meta.order` values on top-level modules (step 10 leaves
      // room for consumer-injected entries). Dashboard sits at 0 so it
      // always lands first; consumer modules registered via `addModules`
      // with `meta.order` in 1..99 slot between Dashboard and Identity,
      // and 200+ for after the last framework module - no consumer
      // mutation required. Override per-route via
      // `defineAdminApp({ routeOrders: { 'dashboard': 5 } })`.
      {
        path: 'dashboard',
        name: 'dashboard',
        component: () => import('../pages/dashboard/Dashboard.vue'),
        meta: {
          title: 'tnzi.admin.modules.dashboard.title',
          // Deliberately NO permission: the dashboard is the baseline landing
          // page every signed-in user must reach (widgets self-filter by
          // their own codes). Gating it made a zero-grant role's members
          // bounce to /403 straight from a successful login.
          keepAlive: true,
          fixedIndexInTab: 0,
          order: 0,
        },
      },

      // ── User Center (self-service profile) ───────────────────
      // No permission needed - any authenticated user can manage their
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
        meta: { title: 'tnzi.admin.modules.identity.label', permission: 'identity.view', order: 100, moduleGate: true },
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
            // One user's page: profile, roles, direct grants, sessions and
            // sign-in history as sections of a single record rather than four
            // overlays stacked on the list.
            path: 'users/:id',
            name: 'identity.users.detail',
            component: () => import('../pages/identity/UserDetail.vue'),
            props: true,
            meta: {
              title: 'tnzi.admin.modules.identity.users.detail.title',
              permission: 'user.view',
              hideInMenu: true,
              activeMenu: 'identity.users',
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
          // GDPR admin has no route: the backend ships no admin GDPR endpoints,
          // and the placeholder page/bridge stubs were removed in the 2026-07-05
          // audit cleanup. Reintroduce page + bridge + route together once the
          // backend ships `DefaultGdprAdminController`.
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
        meta: { title: 'tnzi.admin.modules.authorization.label', permission: 'authorization.view', order: 110, moduleGate: true },
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
        meta: { title: 'tnzi.admin.modules.system.label', permission: 'system.view', order: 120, moduleGate: true },
        children: [
          {
            path: 'dictionaries',
            name: 'system.dictionaries',
            component: () => import('../pages/system/Dictionaries.vue'),
            meta: {
              title: 'tnzi.admin.modules.system.dictionaries.title',
              // Dictionaries render off the same /admin/settings endpoint as
              // Parameters, which the backend gates on system.parameter.view.
              // system.dictionary.view is registered but not enforced there, so
              // the menu must key off the code the endpoint actually checks.
              permission: 'system.parameter.view',
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
              moduleGate: 'feature',
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
      // Module-gated on 'system': the page's backbone (settings-center
      // definitions / save / reset + the Advanced parameters table) lives in
      // the System module backend - a host without it gets a broken page, so
      // the route (and the sidebar gear, which checks this route's
      // availability) hides instead.
      {
        path: 'settings',
        name: 'settings',
        component: () => import('../pages/system/Settings.vue'),
        meta: {
          title: 'tnzi.admin.modules.system.settings.title',
          // The config center spans many modules; no single code gates it.
          // Reachable if the user holds ANY per-group settings view code (or
          // system.parameter.view for Advanced). Enforced by the guard's
          // anySettingsPermission handling + per-group backend filtering.
          anySettingsPermission: true,
          hideInMenu: true,
          moduleGate: 'system',
        },
      },

      // ── Storage ───────────────────────────────────────────────
      {
        path: 'storage',
        name: 'storage',
        meta: { title: 'tnzi.admin.modules.storage.label', permission: 'storage.view', order: 160, moduleGate: true },
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
          {
            path: 'shares',
            name: 'storage.shares',
            component: () => import('../pages/storage/Shares.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.shares.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
          {
            path: 'integrity',
            name: 'storage.integrity',
            component: () => import('../pages/storage/Integrity.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.integrity.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
          {
            path: 'user-usage',
            name: 'storage.userUsage',
            component: () => import('../pages/storage/UserUsage.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.userUsage.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
          {
            path: 'maintenance',
            name: 'storage.maintenance',
            component: () => import('../pages/storage/Maintenance.vue'),
            meta: {
              title: 'tnzi.admin.modules.storage.maintenance.title',
              permission: 'storage.file.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Audit ─────────────────────────────────────────────────
      {
        path: 'audit',
        name: 'audit',
        meta: { title: 'tnzi.admin.modules.audit.label', permission: 'audit.view', order: 130, moduleGate: true },
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
        meta: { title: 'tnzi.admin.modules.notification.label', permission: 'notification.view', order: 170, moduleGate: true },
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
        meta: { title: 'tnzi.admin.modules.chat.label', permission: 'chat.view', order: 140, moduleGate: true },
        children: [
          {
            path: 'overview',
            name: 'chat.overview',
            component: () => import('../pages/chat/Overview.vue'),
            meta: {
              title: 'tnzi.admin.modules.chat.overview.title',
              permission: 'chat.view',
            },
          },
          {
            path: 'conversations',
            name: 'chat.conversations',
            component: () => import('../pages/chat/Conversations.vue'),
            meta: {
              title: 'tnzi.admin.modules.chat.conversations.title',
              permission: 'chat.session.view',
            },
          },
        ],
      },

      // ── Payment ───────────────────────────────────────────────
      {
        path: 'payment',
        name: 'payment',
        meta: { title: 'tnzi.admin.modules.payment.label', permission: 'payment.view', order: 180, moduleGate: true },
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

      // ── Finance ───────────────────────────────────────────────
      // Grouped into accounting sub-menus (Sales / Purchases / Banking /
      // General Ledger / Setup / Reports) so the ~21 pages are scannable
      // instead of one flat wall. The group nodes are component-less menu
      // containers (mirroring the top-level module pattern): clicking one
      // expands it, never navigates. Leaf route NAMES are unchanged so every
      // `router.push({ name })`, `meta.activeMenu` back-reference, tab key and
      // deep-link keeps working; only the URL gains a group segment.
      {
        path: 'finance',
        name: 'finance',
        meta: { title: 'tnzi.admin.modules.finance.label', permission: 'finance.view', order: 185, moduleGate: true },
        children: [
          // ── Sales (AR) ──
          {
            path: 'sales',
            name: 'finance.group.sales',
            meta: { title: 'tnzi.admin.modules.finance.groups.sales' },
            children: [
              {
                path: 'customers',
                name: 'finance.customers',
                component: () => import('../pages/finance/Customers.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.customers.title',
                  permission: 'finance.customer.view',
                  keepAlive: true,
                },
              },
              {
                path: 'customers/:id',
                name: 'finance.customers.detail',
                component: () => import('../pages/finance/CustomerDetail.vue'),
                props: true,
                meta: {
                  title: 'tnzi.admin.modules.finance.party.customerTitle',
                  permission: 'finance.customer.view',
                  hideInMenu: true,
                  activeMenu: 'finance.customers',
                },
              },
              {
                path: 'estimates',
                name: 'finance.estimates',
                component: () => import('../pages/finance/Estimates.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.estimates.title',
                  permission: 'finance.estimate.view',
                  keepAlive: true,
                },
              },
              {
                path: 'invoices',
                name: 'finance.invoices',
                component: () => import('../pages/finance/Invoices.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.invoices.title',
                  permission: 'finance.document.view',
                  keepAlive: true,
                },
              },
              {
                path: 'credit-memos',
                name: 'finance.creditMemos',
                component: () => import('../pages/finance/CreditMemos.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.creditMemos.title',
                  permission: 'finance.document.view',
                  keepAlive: true,
                },
              },
            ],
          },
          // ── Purchases (AP) ──
          {
            path: 'purchases',
            name: 'finance.group.purchases',
            meta: { title: 'tnzi.admin.modules.finance.groups.purchases' },
            children: [
              {
                path: 'vendors',
                name: 'finance.vendors',
                component: () => import('../pages/finance/Vendors.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.vendors.title',
                  permission: 'finance.vendor.view',
                  keepAlive: true,
                },
              },
              {
                path: 'vendors/:id',
                name: 'finance.vendors.detail',
                component: () => import('../pages/finance/VendorDetail.vue'),
                props: true,
                meta: {
                  title: 'tnzi.admin.modules.finance.party.vendorTitle',
                  permission: 'finance.vendor.view',
                  hideInMenu: true,
                  activeMenu: 'finance.vendors',
                },
              },
              {
                path: 'purchase-orders',
                name: 'finance.purchaseOrders',
                component: () => import('../pages/finance/PurchaseOrders.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.purchaseOrders.title',
                  permission: 'finance.purchaseOrder.view',
                  keepAlive: true,
                },
              },
              {
                path: 'bills',
                name: 'finance.bills',
                component: () => import('../pages/finance/Bills.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.bills.title',
                  permission: 'finance.document.view',
                  keepAlive: true,
                },
              },
              {
                path: 'expenses',
                name: 'finance.expenses',
                component: () => import('../pages/finance/Expenses.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.expenses.title',
                  permission: 'finance.document.view',
                  keepAlive: true,
                },
              },
              {
                path: 'receipts',
                name: 'finance.receipts',
                component: () => import('../pages/finance/Receipts.vue'),
                meta: {
                  // 银行域自 2026-07-25 起是独立模块：宿主没加载它时这些页面必须消失，
                  // 而不是渲染出一堆 404 的死链（moduleGate 与权限正交，对超管同样生效）。
                  moduleGate: 'finance-banking',
                  title: 'tnzi.admin.modules.finance.receipts.title',
                  permission: 'finance.receipt.view',
                  keepAlive: true,
                },
              },
            ],
          },
          // ── Banking ──
          {
            path: 'banking',
            name: 'finance.group.banking',
            meta: { title: 'tnzi.admin.modules.finance.groups.banking' },
            children: [
              {
                path: 'bank-accounts',
                name: 'finance.bankAccounts',
                component: () => import('../pages/finance/BankAccounts.vue'),
                meta: {
                  // 银行域自 2026-07-25 起是独立模块：宿主没加载它时这些页面必须消失，
                  // 而不是渲染出一堆 404 的死链（moduleGate 与权限正交，对超管同样生效）。
                  moduleGate: 'finance-banking',
                  title: 'tnzi.admin.modules.finance.bankAccounts.title',
                  permission: 'finance.bankAccount.view',
                  keepAlive: true,
                },
              },
              {
                path: 'payments',
                name: 'finance.payments',
                component: () => import('../pages/finance/Payments.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.payments.title',
                  permission: 'finance.document.view',
                  keepAlive: true,
                },
              },
              {
                path: 'transfers',
                name: 'finance.transfers',
                component: () => import('../pages/finance/Transfers.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.transfers.title',
                  permission: 'finance.document.view',
                  keepAlive: true,
                },
              },
              {
                path: 'reconciliations',
                name: 'finance.reconciliations',
                component: () => import('../pages/finance/Reconciliations.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.reconciliations.title',
                  permission: 'finance.reconciliation.view',
                  keepAlive: true,
                },
              },
              {
                path: 'bank-rules',
                name: 'finance.bankRules',
                component: () => import('../pages/finance/BankRules.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.bankRules.title',
                  permission: 'finance.bankRule.view',
                  moduleGate: 'finance-banking',
                  keepAlive: true,
                },
              },
              {
                path: 'bank-feed',
                name: 'finance.bankFeed',
                component: () => import('../pages/finance/BankFeed.vue'),
                meta: {
                  // 银行域自 2026-07-25 起是独立模块：宿主没加载它时这些页面必须消失，
                  // 而不是渲染出一堆 404 的死链（moduleGate 与权限正交，对超管同样生效）。
                  moduleGate: 'finance-banking',
                  title: 'tnzi.admin.modules.finance.bankFeed.title',
                  permission: 'finance.bankFeed.view',
                  keepAlive: true,
                },
              },
              {
                path: 'checks',
                name: 'finance.checks',
                component: () => import('../pages/finance/Checks.vue'),
                meta: {
                  // 银行域自 2026-07-25 起是独立模块：宿主没加载它时这些页面必须消失，
                  // 而不是渲染出一堆 404 的死链（moduleGate 与权限正交，对超管同样生效）。
                  moduleGate: 'finance-banking',
                  title: 'tnzi.admin.modules.finance.checks.title',
                  permission: 'finance.check.view',
                  keepAlive: true,
                },
              },
              {
                path: 'eft-batches',
                name: 'finance.eftBatches',
                component: () => import('../pages/finance/EftBatches.vue'),
                meta: {
                  // 银行域自 2026-07-25 起是独立模块：宿主没加载它时这些页面必须消失，
                  // 而不是渲染出一堆 404 的死链（moduleGate 与权限正交，对超管同样生效）。
                  moduleGate: 'finance-banking',
                  title: 'tnzi.admin.modules.finance.eftBatches.title',
                  permission: 'finance.eft.view',
                  keepAlive: true,
                },
              },
            ],
          },
          // ── General Ledger (Accountant) ──
          {
            path: 'ledger',
            name: 'finance.group.ledger',
            meta: { title: 'tnzi.admin.modules.finance.groups.ledger' },
            children: [
              {
                path: 'accounts',
                name: 'finance.accounts',
                component: () => import('../pages/finance/Accounts.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.accounts.title',
                  permission: 'finance.account.view',
                  keepAlive: true,
                },
              },
              {
                path: 'journal-entries',
                name: 'finance.journals',
                component: () => import('../pages/finance/JournalEntries.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.journals.title',
                  permission: 'finance.journal.view',
                  keepAlive: true,
                },
              },
              {
                path: 'fiscal-years',
                name: 'finance.fiscalYears',
                component: () => import('../pages/finance/FiscalYears.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.fiscalYears.title',
                  permission: 'finance.fiscalYear.view',
                  keepAlive: true,
                },
              },
              {
                path: 'revaluations',
                name: 'finance.revaluations',
                component: () => import('../pages/finance/Revaluations.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.revaluations.title',
                  permission: 'finance.revaluation.view',
                  keepAlive: true,
                },
              },
            ],
          },
          // ── Setup (master data) ──
          {
            path: 'setup',
            name: 'finance.group.setup',
            meta: { title: 'tnzi.admin.modules.finance.groups.setup' },
            children: [
              {
                path: 'items',
                name: 'finance.items',
                component: () => import('../pages/finance/Items.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.items.title',
                  permission: 'finance.item.view',
                  keepAlive: true,
                },
              },
              {
                path: 'taxes',
                name: 'finance.taxes',
                component: () => import('../pages/finance/Taxes.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.taxes.title',
                  permission: 'finance.tax.view',
                  keepAlive: true,
                },
              },
              {
                path: 'exchange-rates',
                name: 'finance.rates',
                component: () => import('../pages/finance/ExchangeRates.vue'),
                meta: {
                  title: 'tnzi.admin.modules.finance.rates.title',
                  permission: 'finance.rate.view',
                  keepAlive: true,
                },
              },
            ],
          },
          // ── Reports (direct leaf, single page) ──
          {
            path: 'statements',
            name: 'finance.statements',
            component: () => import('../pages/finance/Statements.vue'),
            meta: {
              title: 'tnzi.admin.modules.finance.statements.title',
              permission: 'finance.statement.view',
              keepAlive: true,
            },
          },
          {
            path: 'recurring',
            name: 'finance.recurring',
            component: () => import('../pages/finance/Recurring.vue'),
            meta: {
              // 周期性单据是独立子模块（2026-07-25）：宿主没加载它时这页必须消失，
              // 而不是渲染出一堆 404 的死链。
              moduleGate: 'finance-recurring',
              title: 'tnzi.admin.modules.finance.recurring.title',
              permission: 'finance.recurring.view',
              keepAlive: true,
            },
          },
          {
            path: 'tax-returns',
            name: 'finance.taxReturns',
            component: () => import('../pages/finance/TaxReturns.vue'),
            meta: {
              title: 'tnzi.admin.modules.finance.taxReturns.title',
              permission: 'finance.report.view',
              keepAlive: true,
            },
          },
          {
            path: 'reports',
            name: 'finance.reports',
            component: () => import('../pages/finance/Reports.vue'),
            meta: {
              title: 'tnzi.admin.modules.finance.reports.title',
              permission: 'finance.report.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── Payroll (Tnzi.Finance.Payroll sub-module) ─────────────
      // moduleGate is the explicit backend short name `Finance.Payroll`
      // (normalized → `finance-payroll`), NOT the route name `payroll`, so the
      // menu gates off the actual loaded-module signal from /admin/shell/modules.
      {
        path: 'payroll',
        name: 'payroll',
        meta: { title: 'tnzi.admin.modules.payroll.label', permission: 'payroll.view', order: 186, moduleGate: 'Finance.Payroll' },
        children: [
          {
            path: 'employees',
            name: 'payroll.employees',
            component: () => import('../pages/payroll/Employees.vue'),
            meta: {
              title: 'tnzi.admin.modules.payroll.employees.title',
              permission: 'payroll.employee.view',
              keepAlive: true,
            },
          },
          {
            path: 'setup',
            name: 'payroll.setup',
            component: () => import('../pages/payroll/Setup.vue'),
            meta: {
              title: 'tnzi.admin.modules.payroll.setup.title',
              permission: 'payroll.config.view',
              keepAlive: true,
            },
          },
          {
            path: 'pay-runs',
            name: 'payroll.payRuns',
            component: () => import('../pages/payroll/PayRuns.vue'),
            meta: {
              title: 'tnzi.admin.modules.payroll.payRuns.title',
              permission: 'payroll.run.view',
              keepAlive: true,
            },
          },
        ],
      },

      // ── AI ────────────────────────────────────────────────────
      // Menu order (array order = display order - children aren't
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
        meta: { title: 'tnzi.admin.modules.ai.label', permission: 'ai.view', order: 150, moduleGate: true },
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
            path: 'skills',
            name: 'ai.skills',
            component: () => import('../pages/ai/skills/Skills.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.skills.title',
              permission: 'ai.skill.view',
              keepAlive: true,
              moduleGate: 'AI.Skills',
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
              moduleGate: 'AI.Rag',
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
              moduleGate: 'AI.Workflow',
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
              // Unified MCP control surface: External Servers tab = client
              // registry (ai.mcp.*), This Server + Tool Analytics tabs = self-
              // hosted server (ai.mcpServer.*). Reachable by either view code
              // (OR), each tab's actions gated by its own code.
              permissions: ['ai.mcp.view', 'ai.mcpServer.view'],
              keepAlive: true,
              moduleGate: 'AI.Mcp',
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
              moduleGate: 'AI.Channels',
            },
          },
          {
            path: 'cli-runtimes',
            name: 'ai.cliRuntimes',
            component: () => import('../pages/ai/cli/CliRuntimes.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.cliRuntimes.title',
              permission: 'ai.cliRuntime.view',
              keepAlive: true,
              moduleGate: 'AI.Cli',
            },
          },
          {
            path: 'cli-runs',
            name: 'ai.cliRuns',
            component: () => import('../pages/ai/cli/CliRuns.vue'),
            meta: {
              title: 'tnzi.admin.modules.ai.cliRuns.title',
              permission: 'ai.cliRun.view',
              keepAlive: true,
              moduleGate: 'AI.Cli',
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
              moduleGate: 'AI.Sandbox',
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
        meta: { title: 'tnzi.admin.modules.template.label', permission: 'template.view', order: 190, moduleGate: true },
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
