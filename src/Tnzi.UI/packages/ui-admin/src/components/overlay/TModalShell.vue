<template>
  <!-- Reset the naive theme to the GLOBAL mode. NModal teleports to <body> but
       naive forwards the content area's inner "Card / List" theme through
       provide/inject across the Teleport, so without this an overlay opened
       from a dark-card page would render dark under global light mode. `abstract`
       = renderless (no wrapper DOM to break layout). See useOverlayTheme. -->
  <NConfigProvider abstract :theme="overlayTheme" :theme-overrides="overlayOverrides">
    <NModal
      :show="show"
      preset="card"
      :size="size"
      :mask-closable="maskClosable"
      :auto-focus="autoFocus"
      :title="title"
      :class="{ 't-modal-shell--fullscreen': isFullscreen }"
      :style="modalStyle"
      @update:show="(v: boolean) => emit('update:show', v)"
    >
    <!-- Rich header (entity name + status tag / subtitle): a `#header` slot
         overrides the plain `title` prop for callers that need more than a
         string. Omit it and the `title` prop drives the header as before. -->
    <template v-if="$slots.header" #header>
      <slot name="header" />
    </template>
    <!-- Body scrolls inside the card so long content never pushes the header /
         footer off the viewport. `max-height` = viewport height minus reserved
         header (~56px) + footer (~64px) + outer modal padding (~80px). Short
         content keeps its natural height (native overflow only kicks in past
         max-height). Plain `overflow:auto` (not NScrollbar) so the global
         polish.css macOS-style scrollbar applies - NScrollbar renders an
         overlay thumb that floats over (and occludes) the rightmost widgets. -->
    <div
      class="t-modal-shell__scroll"
      :class="{ 't-modal-shell__scroll--loading': loading }"
      :style="{ maxHeight: contentMaxHeight }"
    >
      <NSpin :show="loading">
        <slot />
      </NSpin>
    </div>
    <template v-if="$slots.footer" #footer>
      <!-- Chrome-level action layout: right-aligned with a uniform gap, so
           pages can drop bare buttons into #footer without them touching. -->
      <div class="t-modal-shell__footer">
        <slot name="footer" />
      </div>
    </template>
    </NModal>
  </NConfigProvider>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NConfigProvider, NModal, NSpin } from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { useOverlayTheme, useOverlayThemeOverrides } from '../../headless/useOverlayTheme'

interface Props {
  /** Open state (controlled). */
  show: boolean
  title?: string
  /** Desktop width (px); capped at 95vw so a too-large value still shows a mask strip. */
  width?: number
  /**
   * Card padding scale (forwarded to naive's card preset). Default `small`
   * (12/16/12px) - the admin-compact chrome. naive's own default is `medium`
   * (19/24/20px), which reads as too roomy for dense admin dialogs. Bump to
   * `medium`/`large` for content-heavy modals.
   */
  size?: 'small' | 'medium' | 'large' | 'huge'
  /**
   * Force fullscreen. When unset, auto-switches to fullscreen on viewports
   * narrower than `max(width + 32, 640)`.
   */
  fullscreen?: boolean
  /** Max viewport height (vh) the inner scroll area may occupy. Default 65. */
  contentMaxHeightVh?: number
  /** Allow closing by clicking the mask. Default false (forms shouldn't lose input on a stray click). */
  maskClosable?: boolean
  /**
   * Show a spinner over the body while its data loads. The body keeps a
   * minimum height during the load so the modal doesn't collapse when the
   * content renders empty (`v-if` on the record).
   */
  loading?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  width: 560,
  size: 'small',
  fullscreen: undefined,
  contentMaxHeightVh: 65,
  maskClosable: false,
  loading: false,
})

const emit = defineEmits<{ 'update:show': [value: boolean] }>()

const bp = useBreakpoint()
const overlayTheme = useOverlayTheme()
const overlayOverrides = useOverlayThemeOverrides()

// Suppress naive's default first-focusable auto-focus on phones: it would grab
// the first input/search box on open and pop the soft keyboard, covering half
// the screen before the user has read anything. Desktop keeps auto-focus (users
// expect to start typing in a create/edit form immediately). `trap-focus` stays
// on either way (a11y).
const autoFocus = computed<boolean>(() => !bp.isSm.value)

// Auto-fullscreen when the viewport can't fit the configured width with room:
//   - width + 32 → never crop at the sides
//   - 640 (sm)  → catch large phones in portrait where even a 560px modal
//                 would visually compete with the whole screen
const isFullscreen = computed<boolean>(() => {
  if (typeof props.fullscreen === 'boolean') return props.fullscreen
  const threshold = Math.max(props.width + 32, 640)
  return bp.width.value > 0 && bp.width.value < threshold
})

const modalStyle = computed(() =>
  isFullscreen.value
    ? { width: '100vw', maxWidth: '100vw' }
    // Cap at 95vw so a too-large `width` still leaves a visible mask strip.
    : { width: `min(${props.width}px, 95vw)` },
)

const contentMaxHeight = computed(() =>
  // Fullscreen leaves room for header (~56) + footer (~64) + safe area (~24).
  // `dvh` so the soft keyboard / mobile address bar shrinks the height instead
  // of the footer buttons ending up below the fold and unreachable.
  isFullscreen.value ? 'calc(100dvh - 144px)' : `${props.contentMaxHeightVh}vh`,
)
</script>

<style scoped>
.t-modal-shell__scroll {
  overflow-y: auto;
  overflow-x: hidden;
}
/* While loading, guarantee the spin container a working height: an empty body
   (content behind `v-if`) would otherwise collapse to ~0 and squash the
   spinner. :deep because NSpin's wrapper is a child component element. */
.t-modal-shell__scroll--loading :deep(.n-spin-container) {
  min-height: 120px;
}
.t-modal-shell__footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  flex-wrap: wrap;
}
/* Phone: stack the action buttons full-width (thumb-friendly). `column` keeps
   DOM order (Cancel above, primary below at thumb reach). naive buttons are
   inline-flex so `align-items: stretch` alone won't widen them - set an explicit
   full width. `:deep` because the buttons are the consumer's, not this scope. */
@media (max-width: 767px) {
  .t-modal-shell__footer {
    flex-direction: column;
    align-items: stretch;
  }
  .t-modal-shell__footer :deep(.n-button) {
    width: 100%;
  }
}
</style>

<!-- Fullscreen targets the teleported modal root, so these rules can't be
     scoped. NModal exposes the outermost wrapper as `.n-modal-container` with
     our custom class merged in. -->
<style>
.t-modal-shell--fullscreen {
  position: fixed !important;
  inset: 0 !important;
  height: 100vh !important;
  height: 100dvh !important; /* dvh: shrink with the mobile URL bar / keyboard */
  max-height: 100vh !important;
  max-height: 100dvh !important;
  border-radius: 0 !important;
  margin: 0 !important;
}
.t-modal-shell--fullscreen .n-card {
  border-radius: 0;
  height: 100vh;
  height: 100dvh;
  max-height: 100vh;
  max-height: 100dvh;
  display: flex;
  flex-direction: column;
}
.t-modal-shell--fullscreen .n-card__content,
.t-modal-shell--fullscreen .n-card-content {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
/* Fullscreen: size the body by what is actually left over, not by a viewport
   guess. `contentMaxHeight` reserves ~64px for the footer, but on phones the
   footer stacks its buttons full-width in a column - a two-button footer is
   ~100px tall. The body's max-height then exceeded the space the card really
   had, and the `overflow: hidden` above clipped the last fields of the form
   with no way to scroll down to them. `!important` because `contentMaxHeight`
   arrives as an inline style. */
.t-modal-shell--fullscreen .t-modal-shell__scroll {
  flex: 1 1 auto;
  min-height: 0;
  max-height: none !important;
}
/* Keep the footer action row clear of the iOS home indicator when fullscreen. */
.t-modal-shell--fullscreen .n-card__footer {
  padding-bottom: max(var(--n-padding-bottom, 16px), env(safe-area-inset-bottom));
}
</style>
