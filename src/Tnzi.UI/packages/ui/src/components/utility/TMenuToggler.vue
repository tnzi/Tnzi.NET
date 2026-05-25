<script setup lang="ts">
/**
 * `TMenuToggler` — sidebar collapse / expand button. Icon points "in" when
 * the sidebar is expanded, "out" when collapsed (matches soybean / antd).
 */
import { computed } from 'vue'
import TButtonIcon from '../display/TButtonIcon.vue'

interface Props {
  collapsed?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  collapsed: false,
  translate: undefined,
})

const emit = defineEmits<{
  toggle: [collapsed: boolean]
  'update:collapsed': [collapsed: boolean]
}>()

function onClick(): void {
  emit('toggle', !props.collapsed)
  emit('update:collapsed', !props.collapsed)
}

const icon = computed(() =>
  props.collapsed ? 'mdi:menu-open' : 'mdi:menu-close',
)
const tooltip = computed(() =>
  props.translate
    ? props.translate(props.collapsed ? 'admin.menu.expand' : 'admin.menu.collapse')
    : props.collapsed
      ? 'Expand menu'
      : 'Collapse menu',
)
</script>

<template>
  <TButtonIcon
    :icon="icon"
    :tooltip="tooltip"
    class="t-menu-toggler"
    @click="onClick"
  />
</template>
