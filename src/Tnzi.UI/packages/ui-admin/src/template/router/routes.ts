import type { RouteRecordRaw } from 'vue-router'

// Phase 2a: default admin routes intentionally emptied. The Phase 1/2a
// scaffold pages were wired to the deleted CrudPage.vue + Dashboard.vue.
// Phase 2b rewrites this file via the new `router/` module (spec §6) and
// ships a soybean-style 3-mode route tree. Consumer apps should inject
// their own `routes` option when calling createAdminRouter() until then.
export const defaultAdminRoutes: RouteRecordRaw[] = []
