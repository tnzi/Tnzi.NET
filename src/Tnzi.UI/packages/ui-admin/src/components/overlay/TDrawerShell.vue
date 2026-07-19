<template>
  <NDrawer
    :show="show"
    :width="effectiveWidth"
    :height="effectiveHeight"
    :placement="effectivePlacement"
    :auto-focus="autoFocus"
    class="t-drawer-shell"
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
import { computed } from 'vue'
import { NDrawer, NDrawerContent } from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'

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
  /**
   * On phones (<768px), present as a bottom sheet instead of a full-width
   * side panel. Default false (side panel goes 100vw = full screen). A bottom
   * sheet reads more natural for quick pickers / short forms.
   */
  mobileBottomSheet?: boolean
  /** Bottom-sheet height when `mobileBottomSheet` is on. Default `'85%'`. */
  mobileSheetHeight?: number | string
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  width: 560,
  placement: 'right',
  closable: true,
  mobileBottomSheet: false,
  mobileSheetHeight: '85%',
})

const emit = defineEmits<{ 'update:show': [value: boolean] }>()

const bp = useBreakpoint()

// naive's NDrawer does NOT clamp width to the viewport, so a fixed 560/640/…/
// 1080px right drawer overflows a 375px phone and pushes ~40% of its content off
// the left edge (clipped). On phones go full-screen (100vw) — or a bottom sheet
// when opted in — mirroring TModalShell's auto-fullscreen. Desktop keeps the
// configured pixel width.
const effectivePlacement = computed<Props['placement']>(() =>
  bp.isSm.value && props.mobileBottomSheet ? 'bottom' : props.placement,
)
const effectiveWidth = computed<number | string>(() =>
  // Phone: full-bleed (100vw) whether side panel or bottom sheet. Desktop keeps
  // the configured pixel width.
  bp.isSm.value ? '100vw' : props.width,
)
const effectiveHeight = computed<number | string | undefined>(() =>
  bp.isSm.value && props.mobileBottomSheet ? props.mobileSheetHeight : undefined,
)

// Suppress naive's default first-focusable auto-focus on phones so opening a
// detail / form drawer never pops the soft keyboard (see TModalShell).
const autoFocus = computed<boolean>(() => !bp.isSm.value)
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
/* Phone: full-screen drawer footer must clear the iOS home indicator, and the
   action buttons stack full-width (thumb-friendly), mirroring the modal/list
   footers. */
@media (max-width: 767px) {
  .t-drawer-shell__footer {
    flex-direction: column;
    align-items: stretch;
    padding-bottom: env(safe-area-inset-bottom);
  }
  /* naive buttons are inline-flex; stretch alone won't widen them. */
  .t-drawer-shell__footer :deep(.n-button) {
    width: 100%;
  }
}
</style>
