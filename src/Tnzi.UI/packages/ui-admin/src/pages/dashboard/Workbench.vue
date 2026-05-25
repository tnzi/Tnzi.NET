<script setup lang="ts">
/**
 * `Workbench` — default landing page for the admin shell.
 *
 * Phase J (0.2.71+): re-implemented as a thin consumer of the widget
 * system (`TWorkbenchLayout` + bundled widgets). Three rendering paths:
 *
 *  1. Consumer passed `defineAdminApp({ workbench: { widgets } })`
 *     → render their widget deck verbatim.
 *  2. No consumer config → render `defaultWorkbenchWidgets()`.
 *  3. Consumer wants to fully replace the page → register their own
 *     route at `/admin/workbench` and ui-admin auto-detects the override.
 */
import { computed } from 'vue'
import TWorkbenchLayout from '../../components/pages/TWorkbenchLayout.vue'
import { useAdminWorkbenchConfig } from '../../plugin/workbenchConfig'
import { defaultWorkbenchWidgets } from '../../widgets/presets'
import type { WidgetDef } from '../../widgets/types'

const config = useAdminWorkbenchConfig()

const widgets = computed<WidgetDef[]>(() => config?.widgets ?? defaultWorkbenchWidgets())
const layout = computed(() => config?.layout ?? 'fixed')
const persistKey = computed(() => config?.persistKey)
const xGap = computed(() => config?.xGap ?? 16)
const yGap = computed(() => config?.yGap ?? 16)
</script>

<template>
  <!-- t-page-scroll: TAdminContent itself no longer scrolls (so CRUD
       pages can pin their pagination); long-form pages opt in by
       wrapping their body with this utility class. -->
  <div class="t-workbench t-page-scroll">
    <TWorkbenchLayout
      :widgets="widgets"
      :layout="layout"
      :persist-key="persistKey"
      :x-gap="xGap"
      :y-gap="yGap"
    />
  </div>
</template>

<style scoped>
.t-workbench {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
</style>
