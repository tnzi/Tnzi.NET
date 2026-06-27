/**
 * Default Iconify icons for the built-in admin routes (Phase I.7.6+).
 *
 * The map is keyed by route `name` (the same identifier used by vue-router and
 * by `useAdminRouteStore.menus`). `useAdminRouteStore` consults this map as
 * the fallback when `route.meta.icon` isn't set — so consumers don't have to
 * sprinkle `icon: '...'` on every route in their `defaultAdminRoutes` override
 * but can still take over per-route by setting `meta.icon` themselves.
 *
 * Picks follow soybean's mdi-style vocabulary (outline variants where
 * available) so the visual feel matches a soybean side-by-side review.
 */
export const DEFAULT_ROUTE_ICONS: Record<string, string> = {
  // ── Top-level dashboard ─────────────────────────────────────────
  dashboard: 'mdi:view-dashboard-outline',

  // ── Top-level module entries ────────────────────────────────────
  identity: 'mdi:account-key-outline',
  authorization: 'mdi:shield-key-outline',
  system: 'mdi:cog-outline',
  storage: 'mdi:database-outline',
  audit: 'mdi:clipboard-text-clock-outline',
  notification: 'mdi:bell-outline',
  chat: 'mdi:message-text-outline',
  payment: 'mdi:credit-card-outline',
  ai: 'mdi:robot-outline',
  template: 'mdi:file-document-outline',

  // ── Identity sub-routes ─────────────────────────────────────────
  'identity.users': 'mdi:account-multiple-outline',
  'identity.roles': 'mdi:account-key-outline',
  'identity.tenants': 'mdi:domain',
  'identity.loginLogs': 'mdi:login-variant',
  'identity.gdpr': 'mdi:shield-account-outline',
  'identity.organizations': 'mdi:office-building-outline',
  'identity.sessions': 'mdi:account-clock-outline',
  'identity.loginSecurity': 'mdi:shield-account-outline',

  // ── Authorization sub-routes ────────────────────────────────────
  'authorization.functionModules': 'mdi:view-grid-outline',
  'authorization.permissions': 'mdi:key-outline',
  'authorization.roleFunctions': 'mdi:account-cog-outline',
  'authorization.entityRoles': 'mdi:account-group-outline',

  // ── System sub-routes ───────────────────────────────────────────
  'system.menus': 'mdi:menu',
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
  'ai.personas': 'mdi:account-tie-outline',
  'ai.evaluations': 'mdi:test-tube',
  'ai.threads': 'mdi:chat-processing-outline',
  'ai.workspaceAgents': 'mdi:folder-account-outline',
  'ai.channels': 'mdi:connection',
  'ai.permissions': 'mdi:shield-key-outline',
  'ai.sandbox': 'mdi:cube-outline',

  // ── Template sub-routes ─────────────────────────────────────────
  'template.templates': 'mdi:file-document-outline',
  'template.layouts': 'mdi:view-dashboard-outline',
}
