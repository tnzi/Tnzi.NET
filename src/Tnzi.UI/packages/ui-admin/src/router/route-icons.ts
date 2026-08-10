/**
 * Default Iconify icons for the built-in admin routes (Phase I.7.6+).
 *
 * The map is keyed by route `name` (the same identifier used by vue-router and
 * by `useAdminRouteStore.menus`). `useAdminRouteStore` consults this map as
 * the fallback when `route.meta.icon` isn't set - so consumers don't have to
 * sprinkle `icon: '...'` on every route in their `defaultAdminRoutes` override
 * but can still take over per-route by setting `meta.icon` themselves.
 *
 * Picks follow soybean's mdi-style vocabulary (outline variants where
 * available) so the visual feel matches a soybean side-by-side review.
 */
export const DEFAULT_ROUTE_ICONS: Record<string, string> = {
  // ── Top-level dashboard ─────────────────────────────────────────
  dashboard: 'mdi:view-dashboard-outline',

  // ── Shell entries (hideInMenu - reached from the shell's own chrome,
  //    not a menu row, but they still surface in the breadcrumb + tab bar) ──
  settings: 'mdi:cog-outline',
  'user-center': 'mdi:account-circle-outline',

  // ── Top-level module entries ────────────────────────────────────
  identity: 'mdi:account-key-outline',
  authorization: 'mdi:shield-key-outline',
  system: 'mdi:cog-outline',
  storage: 'mdi:database-outline',
  audit: 'mdi:clipboard-text-clock-outline',
  notification: 'mdi:bell-outline',
  chat: 'mdi:message-text-outline',
  payment: 'mdi:credit-card-outline',
  finance: 'mdi:scale-balance',
  payroll: 'mdi:account-cash-outline',
  ai: 'mdi:robot-outline',
  template: 'mdi:file-document-outline',

  // ── Identity sub-routes ─────────────────────────────────────────
  'identity.users': 'mdi:account-multiple-outline',
  'identity.roles': 'mdi:account-key-outline',
  'identity.tenants': 'mdi:domain',
  'identity.loginLogs': 'mdi:login-variant',
  'identity.organizations': 'mdi:office-building-outline',
  'identity.sessions': 'mdi:account-clock-outline',
  'identity.loginSecurity': 'mdi:shield-account-outline',

  // ── Authorization sub-routes ────────────────────────────────────
  'authorization.functionModules': 'mdi:view-grid-outline',
  'authorization.permissions': 'mdi:key-outline',
  'authorization.roleFunctions': 'mdi:account-cog-outline',
  'authorization.entityRoles': 'mdi:account-group-outline',

  // ── System sub-routes ───────────────────────────────────────────
  'system.dictionaries': 'mdi:book-alphabet',
  'system.accessLogs': 'mdi:eye-outline',
  'system.scheduledJobs': 'mdi:clock-outline',
  'system.features': 'mdi:toggle-switch-outline',
  'system.logFiles': 'mdi:text-box-search-outline',
  'system.diagnostics': 'mdi:stethoscope',
  'system.performance': 'mdi:speedometer',
  'system.localization': 'mdi:translate',
  'system.signalr': 'mdi:broadcast',
  'system.health': 'mdi:heart-pulse',

  // ── Storage sub-routes ──────────────────────────────────────────
  'storage.files': 'mdi:file-outline',
  'storage.chunks': 'mdi:puzzle-outline',
  'storage.versions': 'mdi:history',
  'storage.shares': 'mdi:share-variant-outline',
  'storage.integrity': 'mdi:shield-check-outline',
  'storage.userUsage': 'mdi:account-details-outline',
  'storage.maintenance': 'mdi:broom',

  // ── Audit sub-routes ────────────────────────────────────────────
  'audit.logs': 'mdi:clipboard-text-outline',
  'audit.operations': 'mdi:cog-refresh-outline',

  // ── Notification sub-routes ─────────────────────────────────────
  'notification.templates': 'mdi:file-document-edit-outline',
  'notification.messages': 'mdi:email-outline',
  'notification.subscriptions': 'mdi:bell-ring-outline',

  // ── Chat sub-routes ─────────────────────────────────────────────
  'chat.overview': 'mdi:view-dashboard-outline',
  'chat.conversations': 'mdi:forum-outline',

  // ── Payment sub-routes ──────────────────────────────────────────
  'payment.orders': 'mdi:cart-outline',
  'payment.subscriptions': 'mdi:credit-card-clock-outline',
  'payment.refunds': 'mdi:cash-refund',
  'payment.statistics': 'mdi:chart-line',
  'payment.invoices': 'mdi:file-document-outline',
  'payment.promotions': 'mdi:ticket-percent-outline',

  // ── Finance sub-routes ──────────────────────────────────────────
  // Finance sub-menu group nodes (component-less containers).
  'finance.group.sales': 'mdi:cart-outline',
  'finance.group.purchases': 'mdi:basket-outline',
  'finance.group.banking': 'mdi:bank-outline',
  'finance.group.ledger': 'mdi:book-open-variant',
  'finance.group.setup': 'mdi:cog-outline',
  'finance.accounts': 'mdi:file-tree-outline',
  'finance.journals': 'mdi:notebook-outline',
  'finance.rates': 'mdi:swap-horizontal',
  'finance.fiscalYears': 'mdi:calendar-lock',
  'finance.customers': 'mdi:account-group-outline',
  'finance.vendors': 'mdi:truck-outline',
  'finance.items': 'mdi:tag-multiple-outline',
  'finance.taxes': 'mdi:cash-multiple',
  'finance.estimates': 'mdi:file-document-edit-outline',
  'finance.invoices': 'mdi:receipt-text-outline',
  'finance.purchaseOrders': 'mdi:clipboard-list-outline',
  'finance.bills': 'mdi:invoice-text-outline',
  'finance.expenses': 'mdi:cash-minus',
  'finance.creditMemos': 'mdi:receipt-text-minus-outline',
  'finance.payments': 'mdi:cash-check',
  'finance.transfers': 'mdi:bank-transfer',
  'finance.reconciliations': 'mdi:scale-balance',
  'finance.statements': 'mdi:file-document-multiple-outline',
  'finance.recurring': 'mdi:calendar-sync-outline',
  'finance.taxReturns': 'mdi:file-percent-outline',
  'finance.bankAccounts': 'mdi:bank-outline',
  'finance.bankRules': 'mdi:filter-cog-outline',
  'finance.bankFeed': 'mdi:bank-transfer-in',
  'finance.checks': 'mdi:checkbook',
  'finance.eftBatches': 'mdi:bank-transfer-out',
  'finance.receipts': 'mdi:receipt-text-check-outline',
  'finance.revaluations': 'mdi:currency-usd-off',
  'finance.reports': 'mdi:chart-box-outline',

  // ── Payroll sub-routes ──────────────────────────────────────────
  'payroll.employees': 'mdi:account-group-outline',
  'payroll.setup': 'mdi:cog-outline',
  'payroll.payRuns': 'mdi:cash-multiple',

  // ── AI sub-routes ───────────────────────────────────────────────
  'ai.agents': 'mdi:robot-outline',
  'ai.workflows': 'mdi:source-branch',
  'ai.workflowRuns': 'mdi:play-circle-outline',
  'ai.skills': 'mdi:lightbulb-on-outline',
  'ai.providers': 'mdi:cloud-cog-outline',
  'ai.usage': 'mdi:chart-line',
  'ai.knowledge': 'mdi:book-open-page-variant-outline',
  'ai.mcp': 'mdi:server-network-outline',
  'ai.quota': 'mdi:speedometer',
  'ai.evaluations': 'mdi:test-tube',
  'ai.threads': 'mdi:chat-processing-outline',
  'ai.workspaceAgents': 'mdi:folder-account-outline',
  'ai.channels': 'mdi:connection',
  'ai.permissions': 'mdi:shield-key-outline',
  'ai.sandbox': 'mdi:cube-outline',
  'ai.cliRuntimes': 'mdi:console',
  'ai.cliRuns': 'mdi:console-line',

  // ── Template sub-routes ─────────────────────────────────────────
  'template.templates': 'mdi:file-document-outline',
  'template.layouts': 'mdi:view-dashboard-outline',

  // ── Signing sub-routes ──────────────────────────────────────────
  signing: 'mdi:draw-pen',
  'signing.requests': 'mdi:file-sign',
  'signing.templates': 'mdi:file-document-edit-outline',
}
