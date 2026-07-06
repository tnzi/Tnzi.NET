<template>
  <NDrawer
    :show="show"
    :width="width"
    :placement="placement"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <NDrawerContent :title="title" :closable="closable">
      <slot />
      <template v-if="$slots.footer" #footer>
        <slot name="footer" />
      </template>
    </NDrawerContent>
  </NDrawer>
</template>

<script setup lang="ts">
import { NDrawer, NDrawerContent } from 'naive-ui'

interface Props {
  /** Open state (controlled). */
  show: boolean
  title?: string
  /** Drawer width (px) or a CSS string (e.g. `'100vw'` for phone full-screen). Default 560. */
  width?: number | string
  /** Slide-in edge. Default `right`. */
  placement?: 'top' | 'right' | 'bottom' | 'left'
  /** Show the built-in close (X) affordance in the header. Default true. */
  closable?: boolean
}

withDefaults(defineProps<Props>(), {
  title: undefined,
  width: 560,
  placement: 'right',
  closable: true,
})

const emit = defineEmits<{ 'update:show': [value: boolean] }>()
</script>
