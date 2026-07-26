<script setup lang="ts">
/**
 * `Dashboard` - default landing page for the admin shell.
 *
 * Phase J (0.2.71+): a thin consumer of the generic Workbench widget-grid
 * primitive (`TWorkbenchLayout` + bundled widgets). Three rendering paths:
 *
 *  1. Consumer passed `defineAdminApp({ dashboard: { widgets } })`
 *     → render their widget deck verbatim.
 *  2. No consumer config → render `defaultWorkbenchWidgets()`.
 *  3. Consumer wants to fully replace the page → register their own
 *     route at `/admin/dashboard` and ui-admin auto-detects the override.
 */
import { computed } from 'vue'
import TWorkbenchLayout from '../../components/pages/TWorkbenchLayout.vue'
import TContentPage from '../../components/layout/TContentPage.vue'
import { useAdminDashboardConfig } from '../../plugin/dashboardConfig'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useModuleAvailability } from '../../headless/useModuleAvailability'
import { defaultWorkbenchWidgets } from '../../widgets/presets'
import type { WidgetDef } from '../../widgets/types'

const config = useAdminDashboardConfig()
const authStore = useAdminAuthStore()
const moduleAvailability = useModuleAvailability()

// Two-dimensional widget deck filter:
//
// 1. MODULE availability (`WidgetDef.module`) - a widget whose data lives in a
//    framework module the backend never loaded is dropped for EVERYONE (no
//    super-user bypass: the endpoints 404 for the super admin too). Uses
//    `canActivate`, so module-tagged widgets defer while the availability
//    probe is in flight instead of racing it with doomed fetches; once the
//    signal settles they appear reactively (fail-open on old backends).
// 2. PERMISSION (`WidgetDef.permission`) - the same model as the sidebar
//    filter: super users and the pre-permission-load window see everything
//    (fail-open), otherwise a widget declaring a `permission` the user lacks
//    is dropped and never mounts.
//
// Widgets carrying neither field (business or mixed tiles that themselves
// degrade gracefully) always render.
const widgets = computed<WidgetDef[]>(() => {
  const all = config?.widgets ?? defaultWorkbenchWidgets()
  const moduleVisible = all.filter(
    (w) => !w.module || moduleAvailability.canActivate(w.module),
  )
  const bypass = authStore.isSuperUser || authStore.userInfo === null
  if (bypass) return moduleVisible
  return moduleVisible.filter((w) => !w.permission || authStore.hasPermission(w.permission))
})
const layout = computed(() => config?.layout ?? 'fixed')
const persistKey = computed(() => config?.persistKey)
const xGap = computed(() => config?.xGap ?? 16)
const yGap = computed(() => config?.yGap ?? 16)

// The dashboard renders its own greeting banner via TWorkbenchLayout/
// THeaderBanner, so we suppress the TContentPage header (show-header=false).
const t = (key: string) => key
</script>

<template>
  <TContentPage :show-header="false" :translate="t" scroll="auto">
    <TWorkbenchLayout
      :widgets="widgets"
      :layout="layout"
      :persist-key="persistKey"
      :x-gap="xGap"
      :y-gap="yGap"
    />
  </TContentPage>
</template>
