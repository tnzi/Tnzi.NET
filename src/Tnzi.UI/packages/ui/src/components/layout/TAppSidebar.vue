<script setup lang="ts">
/**
 * @deprecated TAppSidebar is a backend-admin-specific component that pollutes the C-end @tnzi/ui package.
 * It will be removed in Phase 2 of the 2026-04-12 production-readiness refactor. Use
 * `@tnzi/ui-admin`'s `TAdminSidebar` (with vertical/vertical-mix/horizontal mode support) instead.
 * See docs/superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md §D4.
 */
import { computed } from 'vue'
import { NLayoutSider, NMenu } from 'naive-ui'
import type { IMenuItem } from '@tnzi/core'
import { convertToMenuOptions } from '../../utils/naive-helpers'

interface Props {
  collapsed?: boolean
  width?: number
  collapsedWidth?: number
  menuItems?: IMenuItem[]
  showLogo?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  collapsed: false,
  width: 220,
  collapsedWidth: 64,
  showLogo: true,
})

const emit = defineEmits<{
  'update:collapsed': [value: boolean]
  menuSelect: [key: string]
}>()

const menuOptions = computed(() => {
  if (!props.menuItems) return []
  return convertToMenuOptions(props.menuItems)
})

function handleMenuSelect(key: string) {
  emit('menuSelect', key)
}
</script>

<template>
  <NLayoutSider
    :collapsed="collapsed"
    :width="width"
    :collapsed-width="collapsedWidth"
    bordered
    show-trigger
    collapse-mode="width"
    @collapse="emit('update:collapsed', true)"
    @expand="emit('update:collapsed', false)"
  >
    <div v-if="showLogo" class="flex-center h-[56px] px-3 overflow-hidden border-b border-naive">
      <slot name="logo" />
    </div>
    <NMenu
      :options="menuOptions"
      @update:value="handleMenuSelect"
    />
    <div class="mt-auto py-2 px-3">
      <slot name="footer" />
    </div>
  </NLayoutSider>
</template>
