<script setup lang="ts">
/**
 * `Dashboard` — default landing page for the admin shell.
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
import { defaultWorkbenchWidgets } from '../../widgets/presets'
import type { WidgetDef } from '../../widgets/types'

const config = useAdminDashboardConfig()

const widgets = computed<WidgetDef[]>(() => config?.widgets ?? defaultWorkbenchWidgets())
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
