<script setup lang="ts">
/**
 * `TWidgetCard` admin wrapper.
 *
 * The card implementation lives in `@tnzi/ui/components/layout`
 * (sunk in 0.2.x). This wrapper injects the admin-side `translatePageKey`
 * helper plus the localised tooltip strings (`admin.common.reload`,
 * `admin.widgets.dragHandle`, `admin.common.error`) so existing admin
 * widgets pick up i18n resolution without passing the translator
 * explicitly.
 *
 * New code outside the admin shell should import directly from
 * `@tnzi/ui`:
 *
 *   import { TWidgetCard } from '@tnzi/ui'
 */
import { computed } from 'vue'
import { TWidgetCard as TWidgetCardBase } from '@tnzi/ui'
import { translatePageKey } from '../../i18n/translate'

interface Props {
  id: string
  title?: string
  icon?: string
  height?: number | 'auto'
  refreshable?: boolean
  bare?: boolean
  draggable?: boolean
}

withDefaults(defineProps<Props>(), {
  title: undefined,
  icon: undefined,
  height: 'auto',
  refreshable: true,
  bare: false,
  draggable: false,
})

defineEmits<{
  refresh: []
}>()

const adminTranslate = (key: string): string => translatePageKey('', key)

const refreshLabel = computed(() => translatePageKey('', 'admin.common.reload') || 'Refresh')
const dragLabel = computed(() => translatePageKey('', 'admin.widgets.dragHandle') || 'Drag to reorder')
const errorLabel = computed(() => translatePageKey('', 'admin.common.error') || 'Error')
</script>

<template>
  <TWidgetCardBase
    :id="id"
    :title="title"
    :icon="icon"
    :height="height"
    :refreshable="refreshable"
    :bare="bare"
    :draggable="draggable"
    :translate="adminTranslate"
    :refresh-label="refreshLabel"
    :drag-label="dragLabel"
    :error-label="errorLabel"
    @refresh="$emit('refresh')"
  >
    <template v-if="$slots['header-extra']" #header-extra>
      <slot name="header-extra" />
    </template>
    <slot />
  </TWidgetCardBase>
</template>
