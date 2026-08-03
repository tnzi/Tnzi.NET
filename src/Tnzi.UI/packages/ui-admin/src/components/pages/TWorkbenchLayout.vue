<script setup lang="ts">
/**
 * `TWorkbenchLayout` admin wrapper.
 *
 * The layout implementation lives in `@tnzi/ui/components/layout`
 * (sunk in 0.2.x). This wrapper injects the admin-side `translatePageKey`
 * helper plus the localised tooltip strings so existing admin workbench
 * cards pick up i18n resolution without passing the translator
 * explicitly.
 *
 * New code outside the admin shell should import directly from
 * `@tnzi/ui`:
 *
 *   import { TWorkbenchLayout, type WidgetDef } from '@tnzi/ui'
 */
import { computed } from 'vue'
import { TWorkbenchLayout as TWorkbenchLayoutBase, type WidgetDef, type WorkbenchConfig } from '@tnzi/ui'
import { translatePageKey } from '../../i18n/translate'

interface Props {
  widgets: WidgetDef[]
  layout?: WorkbenchConfig['layout']
  persistKey?: string
  xGap?: number
  yGap?: number
  hasPermission?: (key: string) => boolean
}

// Back-compat: the admin shell used to default the persisted-layout key to
// `'tnzi-admin-workbench-order'` (when this hook lived in ui-admin). The ui
// base now defaults to a more generic `'tnzi-workbench-order'`, so we
// re-apply the admin prefix here to keep existing users' saved drag-order
// from silently resetting on upgrade.
const ADMIN_DEFAULT_PERSIST_KEY = 'tnzi-admin-workbench-order'

const props = withDefaults(defineProps<Props>(), {
  layout: 'fixed',
  persistKey: undefined,
  xGap: 16,
  yGap: 16,
  hasPermission: undefined,
})

const resolvedPersistKey = computed(() => props.persistKey ?? ADMIN_DEFAULT_PERSIST_KEY)

const adminTranslate = (key: string): string => translatePageKey('', key)

const refreshLabel = computed(() => translatePageKey('', 'admin.common.reload') || 'Refresh')
const dragLabel = computed(() => translatePageKey('', 'admin.widgets.dragHandle') || 'Drag to reorder')
const errorLabel = computed(() => translatePageKey('', 'admin.common.error') || 'Error')
</script>

<template>
  <TWorkbenchLayoutBase
    :widgets="widgets"
    :layout="layout"
    :persist-key="resolvedPersistKey"
    :x-gap="xGap"
    :y-gap="yGap"
    :has-permission="hasPermission"
    :translate="adminTranslate"
    :refresh-label="refreshLabel"
    :drag-label="dragLabel"
    :error-label="errorLabel"
  >
    <template v-if="$slots.header" #header>
      <slot name="header" />
    </template>
    <template v-if="$slots.footer" #footer>
      <slot name="footer" />
    </template>
  </TWorkbenchLayoutBase>
</template>
