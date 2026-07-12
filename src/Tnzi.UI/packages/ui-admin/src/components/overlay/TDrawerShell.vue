<template>
  <NDrawer
    :show="show"
    :width="width"
    :placement="placement"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <NDrawerContent :title="title" :closable="closable">
      <!-- Rich header (title + tag / info popover): a `#header` slot overrides the
           plain `title` prop for callers that need more than a string. Omit it and
           the `title` prop drives the header as before. -->
      <template v-if="$slots.header" #header>
        <slot name="header" />
      </template>
      <slot />
      <template v-if="$slots.footer" #footer>
        <!-- Same chrome-level action layout as TModalShell: bare buttons in
             #footer get a uniform right-aligned gap instead of touching. -->
        <div class="t-drawer-shell__footer">
          <slot name="footer" />
        </div>
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

<style scoped>
.t-drawer-shell__footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  flex-wrap: wrap;
  width: 100%;
}
</style>
