<script setup lang="ts">
/**
 * `TMenuToggler` - sidebar collapse / expand button. Faithful port of
 * soybean-admin's `MenuToggler` (src/components/common/menu-toggler.vue):
 * the icon is an animated `line-md` fold glyph whose stroke draws itself on
 * mount, and the button carries `:key="String(collapsed)"` so it remounts on
 * every toggle - replaying that draw-in animation each click. `fold-left`
 * ("click to collapse") shows when expanded, `fold-right` ("click to expand")
 * when collapsed.
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
  props.collapsed ? 'line-md:menu-fold-right' : 'line-md:menu-fold-left',
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
    :key="String(collapsed)"
    :icon="icon"
    :tooltip="tooltip"
    class="t-menu-toggler"
    @click="onClick"
  />
</template>
