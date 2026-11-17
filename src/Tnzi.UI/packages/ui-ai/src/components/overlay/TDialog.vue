<script setup lang="ts">
/**
 * @experimental
 * TDialog - the package's general dialog: a title, a close button, a body that
 * scrolls inside the dialog, and an optional footer of actions.
 *
 * Drawn from the shared chrome in `dialog.css` (`.t-dialog-backdrop`,
 * `.t-dialog`, `.t-dialog__header` / `__title` / `__description` / `__close` /
 * `__body` / `__footer`), the same pieces `TSettingsDialog` and
 * `TCommandPalette` use, so every dialog in a product built on this package has
 * the same padding, close button, scrollbar and backdrop.
 *
 * Behaviour:
 * - Escape, the close button and a click on the backdrop ask to close
 *   (`update:modelValue` false). `closeOnEscape` / `closeOnBackdrop` turn the
 *   first and last off; `busy` holds the dialog open entirely (a save in
 *   flight) and disables the close button. Setting `modelValue` false always
 *   closes it.
 * - It is a layer of the page's overlay stack (`useFocusTrap` from `@tnzi/ui`):
 *   Tab stays inside, and a dialog opened over it gets Escape and Tab first.
 *   The dialog opened last is drawn on top (`--tnzi-ai-z-dialog` plus its depth
 *   in the stack of open dialogs); menus opened from it sit above it.
 * - Opening moves focus to an `autofocus` / `data-autofocus` element, else the
 *   first field, else the panel itself; never to the close button. Closing
 *   returns focus to what had it.
 * - The page behind does not scroll while it is open.
 * - Below `narrowBreakpoint` it is a sheet rising from the bottom
 *   (`narrowLayout="fullscreen"` for a full screen), padded for the device's
 *   safe areas. Its content keeps clear of `--tnzi-overlay-inset-top` while the
 *   backdrop covers the whole viewport.
 * - The sheet and the full screen keep to the part of the window the user can
 *   see (the visual viewport, `useVisualViewportInsets`): with the on-screen
 *   keyboard open, the header and footer stay above it and the body scrolls in
 *   what is left; a focused field inside is scrolled back into view when that
 *   space changes. It follows the keyboard opening and closing and the page
 *   moving under it. The centred dialog, and every layout in a browser without
 *   `visualViewport`, are laid out against the whole window as before.
 *
 * - On the sheet and the full screen (a phone), the back button or gesture
 *   closes the dialog the way Escape does, instead of leaving the page
 *   (`useBackLayer`); one back per dialog when several are stacked. A dialog
 *   that does not close on Escape, has no close button (`closable: false`), is
 *   `busy` or whose `beforeClose` says no stays open and keeps the page.
 *   `closeOnBack: false` leaves back to the page: for a dialog whose open state
 *   is already kept in the address, where back closes it by itself.
 *
 * Special cases: `flush` (no body padding: a preview, an image), `#header`
 * (replaces the title block; the close button stays unless `closable` is
 * false), `width` / `height`, `fullscreen`, `closable: false`.
 *
 * Motion:
 * - `origin` (an element, `{ element, rotate }`, a box, or a function that
 *   finds one) makes the dialog grow out of it and shrink back into it: the
 *   panel's place, size and tilt, its contents fading in once it is nearly
 *   there. The origin is asked again on close, so a note saved meanwhile can be
 *   found on the board; when it is gone (deleted, off screen) the dialog falls
 *   back to its own fade and rise. With reduced motion it only fades.
 * - `transition="none"` turns the built-in enter and leave off and nothing
 *   else (backdrop, stacking, focus trap, Escape and the phone sheet stay);
 *   the panel is exposed (`panel`) for a page that animates it itself.
 * - `beforeClose(reason)` runs before every close the dialog itself asks for
 *   (Escape, backdrop, close button, the `close()` given to slots and exposed);
 *   it may be async, and returning `false` keeps the dialog open. A parent
 *   that sets `modelValue` false has already decided: the leave animation still
 *   runs before the dialog is removed, but it cannot be cancelled.
 */
import { computed, onBeforeUnmount, ref, useId, useSlots, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useFocusTrap } from '@tnzi/ui/headless'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'
import { useVisualViewportInsets } from '../../headless/useVisualViewportInsets'
import { useViewportBelow } from '../../headless/useViewportBelow'
import { useControlsText } from '../controls/controls-text'
import {
  cssLength,
  dialogInitialFocus,
  dialogWidth,
  type DialogNarrowLayout,
  type DialogSize,
} from './dialog'
import {
  chooseMotion,
  playEnter,
  playLeave,
  prefersReducedMotion,
  resolveOrigin,
  type DialogCloseReason,
  type DialogOriginSource,
} from './dialog-motion'
import { useDialogLayer } from './layers'
import { useBackLayer } from '../../headless/back-layers'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    /** The dialog's heading and accessible name. */
    title?: string
    /** One short line under the title. Use sparingly: most dialogs say enough in the title. */
    description?: string
    /** Accessible name when there is no `title` (a custom `#header` without a heading). */
    ariaLabel?: string
    /** `alertdialog` for a confirmation that interrupts the user. */
    role?: 'dialog' | 'alertdialog'
    /** Width on a wide screen: `sm` 400, `md` 520, `lg` 720, `xl` 960 (the `--tnzi-ai-dialog-width-*` tokens). */
    size?: DialogSize
    /** A width of its own (number = px), overriding `size`. Never wider than the window. */
    width?: number | string
    /** A fixed height (number = px), e.g. for a preview; by default the dialog fits its content. */
    height?: number | string
    /** Fill the window (below its top inset) on every screen. */
    fullscreen?: boolean
    /** No padding around the body: edge-to-edge content. */
    flush?: boolean
    /** Show the close button. Escape and the backdrop are separate (`closeOnEscape`, `closeOnBackdrop`). */
    closable?: boolean
    closeOnEscape?: boolean
    closeOnBackdrop?: boolean
    /**
     * On the sheet and the full screen, back closes the dialog (as Escape does)
     * rather than leaving the page. Default true; false for a dialog kept in the address.
     */
    closeOnBack?: boolean
    /** Holds the dialog open (a save in flight): no Escape, no backdrop, close button disabled. */
    busy?: boolean
    /** Viewport width (px) below which the narrow layout applies. Default 768, as `TSettingsDialog`. */
    narrowBreakpoint?: number
    /** Force the narrow layout (`true`) or the wide one (`false`); unset follows `narrowBreakpoint`. */
    narrow?: boolean
    /** The narrow layout: a bottom sheet (default) or the full screen. */
    narrowLayout?: DialogNarrowLayout
    /** The element to focus on open, instead of the first field. */
    initialFocus?: () => HTMLElement | null
    /** What the dialog opens from and closes back into (see Motion above). */
    origin?: DialogOriginSource
    /** `none`: no built-in enter / leave animation, for a page that animates the panel itself. */
    transition?: 'default' | 'none'
    /** Runs before a close the dialog asks for; `false` (or a promise of it) keeps it open. */
    beforeClose?: (reason: DialogCloseReason) => boolean | void | Promise<boolean | void>
  }>(),
  {
    title: undefined,
    description: undefined,
    ariaLabel: undefined,
    role: 'dialog',
    size: 'md',
    width: undefined,
    height: undefined,
    fullscreen: false,
    flush: false,
    closable: true,
    closeOnEscape: true,
    closeOnBackdrop: true,
    closeOnBack: true,
    busy: false,
    narrowBreakpoint: 768,
    narrow: undefined,
    narrowLayout: 'sheet',
    initialFocus: undefined,
    origin: undefined,
    transition: 'default',
    beforeClose: undefined,
  },
)

/* The root is a Teleport, which cannot take attributes: `class`, `data-*` and
   the like go on the panel instead. */
defineOptions({ inheritAttrs: false })

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  /** After the enter animation finished. */
  opened: []
  /** After the dialog has closed and its leave transition finished, by any means. */
  closed: []
}>()

defineSlots<{
  default?: (props: { close: () => Promise<boolean> }) => unknown
  header?: (props: { close: () => Promise<boolean>; titleId: string }) => unknown
  footer?: (props: { close: () => Promise<boolean> }) => unknown
}>()

const slots = useSlots()
const t = useControlsText()
const titleId = `t-dialog-title-${useId()}`
const descriptionId = `t-dialog-desc-${useId()}`

const host = ref<HTMLElement | null>(null)
const panel = ref<HTMLElement | null>(null)
const body = ref<HTMLElement | null>(null)

const viewportNarrow = useViewportBelow(() => props.narrowBreakpoint)
const isNarrow = computed(() => props.narrow ?? viewportNarrow.value)
const layout = computed<'center' | 'sheet' | 'fullscreen'>(() => {
  if (props.fullscreen) return 'fullscreen'
  if (isNarrow.value) return props.narrowLayout === 'fullscreen' ? 'fullscreen' : 'sheet'
  return 'center'
})

const hasHeader = computed(() => Boolean(props.title || props.description || slots.header || props.closable))
const labelledBy = computed(() => (props.title || slots.header ? titleId : undefined))

/* A close in progress (waiting for `beforeClose`): a second request joins it. */
let pending: Promise<boolean> | null = null

/**
 * Ask to close. Honoured unless the dialog is busy or `beforeClose` says no;
 * resolves to whether it closes. A `beforeClose` that throws keeps the dialog
 * open and the returned promise rejects with its error.
 */
function requestClose(reason: DialogCloseReason): Promise<boolean> {
  if (props.busy || !props.modelValue) return Promise.resolve(false)
  if (pending) return pending
  /* No hook: close at once, as before there was one. */
  if (!props.beforeClose) {
    emit('update:modelValue', false)
    return Promise.resolve(true)
  }
  pending = (async () => {
    try {
      const verdict = props.beforeClose ? await props.beforeClose(reason) : undefined
      if (verdict === false || props.busy) return false
      emit('update:modelValue', false)
      return true
    } finally {
      pending = null
    }
  })()
  return pending
}

/** The close given to slots and exposed. */
const close = (): Promise<boolean> => requestClose('programmatic')

/* The dialog layer plus this dialog's depth in the stack of open dialogs. */
const zIndex = useDialogLayer(() => props.modelValue)
const hostStyle = computed(() => ({ zIndex: zIndex.value }))

const panelStyle = computed(() => {
  const style: Record<string, string> = { '--t-dialog-width': dialogWidth(props.size, props.width) }
  const height = cssLength(props.height)
  if (height) style['--t-dialog-height'] = height
  return style
})

useFocusTrap(panel, () => props.modelValue, {
  onEscape: () => {
    if (props.closeOnEscape) void requestClose('escape')
  },
  initialFocus: () => props.initialFocus?.() ?? (panel.value ? dialogInitialFocus(panel.value) : null),
})

useBodyScrollLock(() => props.modelValue)

/* Back on a phone: the close path Escape takes, held to the same rules. A
   refusal keeps the dialog and the page (the layer gets its entry back). */
useBackLayer(
  () => props.modelValue,
  () => (props.closable && props.closeOnEscape ? requestClose('back') : false),
  { enabled: () => props.closeOnBack && layout.value !== 'center' },
)

/* The sheet and the full screen fit the visible part of the window (above the
   keyboard). The variables live on the host, which goes away with the dialog,
   so nothing is left on the document. When that part changes, a focused field
   in the panel is brought back into view inside the body that now scrolls. */
useVisualViewportInsets(host, () => layout.value !== 'center', {
  onChange: () => {
    const focused = typeof document === 'undefined' ? null : document.activeElement
    if (focused instanceof HTMLElement && focused !== panel.value && panel.value?.contains(focused)) {
      focused.scrollIntoView({ block: 'nearest', inline: 'nearest' })
    }
  },
})

/* A click closes only when the press also started on the backdrop: selecting
   text in a field and letting go outside the panel is not a request to close. */
let pressedOnBackdrop = false
function onBackdropDown(event: MouseEvent): void {
  pressedOnBackdrop = event.target === event.currentTarget
}
function onBackdropClick(event: MouseEvent): void {
  const fromBackdrop = pressedOnBackdrop && event.target === event.currentTarget
  pressedOnBackdrop = false
  if (fromBackdrop && props.closeOnBackdrop) void requestClose('backdrop')
}

/* Hairlines under the header and over the footer only while content is
   scrolled under them. Re-measured on scroll and whenever the body or what is
   in it changes size. */
const scrolledFromTop = ref(false)
const scrolledFromBottom = ref(false)

function measure(): void {
  const el = body.value
  if (!el) return
  scrolledFromTop.value = el.scrollTop > 0
  scrolledFromBottom.value = el.scrollTop + el.clientHeight < el.scrollHeight - 1
}

let resizeObserver: ResizeObserver | null = null
let mutationObserver: MutationObserver | null = null

function observe(el: HTMLElement): void {
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => measure())
    resizeObserver.observe(el)
    for (const child of Array.from(el.children)) resizeObserver.observe(child)
  }
  if (typeof MutationObserver !== 'undefined') {
    mutationObserver = new MutationObserver(() => {
      if (resizeObserver) for (const child of Array.from(el.children)) resizeObserver.observe(child)
      measure()
    })
    mutationObserver.observe(el, { childList: true })
  }
  measure()
}

function unobserve(): void {
  resizeObserver?.disconnect()
  mutationObserver?.disconnect()
  resizeObserver = null
  mutationObserver = null
}

watch(body, (el) => {
  unobserve()
  if (el) observe(el)
}, { flush: 'post' })

onBeforeUnmount(unobserve)

/* -- Motion: the built-in CSS fade and rise, or JS hooks for an origin and for
   `transition="none"`. The origin is resolved when the dialog enters and again
   when it leaves. -- */
const motionByScript = computed(() => props.transition === 'none' || props.origin != null)

function viewport(): { width: number; height: number } {
  return { width: window.innerWidth, height: window.innerHeight }
}

function panelOf(host: Element): HTMLElement | null {
  return (Array.from(host.children).find((child) => child.classList.contains('t-dialog')) as HTMLElement | undefined) ?? null
}

function onEnter(host: Element, done: () => void): void {
  const panelEl = panelOf(host)
  if (props.transition === 'none' || !panelEl) return done()
  const motion = chooseMotion(resolveOrigin(props.origin, viewport()), prefersReducedMotion())
  void playEnter(host as HTMLElement, panelEl, motion).finally(done)
}

function onLeave(host: Element, done: () => void): void {
  const panelEl = panelOf(host)
  if (props.transition === 'none' || !panelEl) return done()
  const motion = chooseMotion(resolveOrigin(props.origin, viewport()), prefersReducedMotion())
  void playLeave(host as HTMLElement, panelEl, motion).finally(done)
}

const transitionHooks = computed(() => (motionByScript.value ? { onEnter, onLeave } : {}))

/* `panel` is the `.t-dialog` element while the dialog is shown (exposed refs
   are unwrapped: `dialogRef.value.panel` is the element). */
defineExpose({ close, panel })
</script>

<template>
  <Teleport to="body">
    <transition
      name="t-dialog-fade"
      :css="!motionByScript"
      v-bind="transitionHooks"
      @after-enter="emit('opened')"
      @after-leave="emit('closed')"
    >
      <div
        v-if="modelValue"
        ref="host"
        class="t-dialog-host t-dialog-backdrop t-ai-scroll-scope"
        :class="`t-dialog-host--${layout}`"
        :style="hostStyle"
        @mousedown="onBackdropDown"
        @click="onBackdropClick"
      >
        <div
          ref="panel"
          v-bind="$attrs"
          class="t-dialog t-dialog-host__panel"
          :class="{
            't-dialog--narrow': isNarrow,
            't-dialog-host__panel--sized': height !== undefined && height !== '',
          }"
          :style="panelStyle"
          :role="role"
          aria-modal="true"
          :aria-labelledby="labelledBy"
          :aria-describedby="description ? descriptionId : undefined"
          :aria-label="labelledBy ? undefined : ariaLabel"
          :aria-busy="busy || undefined"
          tabindex="-1"
        >
          <header
            v-if="hasHeader"
            class="t-dialog__header"
            :class="{ 't-dialog__header--plain': !closable, 'is-divided': scrolledFromTop }"
          >
            <slot name="header" :close="close" :title-id="titleId">
              <div class="t-dialog__heading">
                <h2 v-if="title" :id="titleId" class="t-dialog__title">{{ title }}</h2>
                <p v-if="description" :id="descriptionId" class="t-dialog__description">{{ description }}</p>
              </div>
            </slot>
            <button
              v-if="closable"
              type="button"
              class="t-dialog__close"
              :aria-label="t.close"
              :disabled="busy"
              @click="requestClose('close-button')"
            >
              <Icon icon="lucide:x" aria-hidden="true" />
            </button>
          </header>
          <div
            ref="body"
            class="t-dialog__body t-ai-scroll"
            :class="{ 't-dialog__body--flush': flush, 't-dialog__body--headless': !hasHeader }"
            @scroll.passive="measure"
          >
            <slot :close="close" />
          </div>
          <footer v-if="$slots.footer" class="t-dialog__footer" :class="{ 'is-divided': scrolledFromBottom }">
            <slot name="footer" :close="close" />
          </footer>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<style scoped>
/* Placement only; the chrome is the shared `dialog.css`. The backdrop pads for
   the page's top inset, so the dialog centres in the space below it. */
.t-dialog-host {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: calc(24px + var(--tnzi-overlay-inset-top, 0px)) 24px 24px;
}
.t-dialog-host__panel {
  width: min(var(--t-dialog-width), 100%);
  max-height: min(100%, var(--tnzi-ai-dialog-max-height, 860px));
}
.t-dialog-host__panel--sized {
  height: min(var(--t-dialog-height), 100%);
}

/* -- The sheet and the full screen keep to the visible part of the window:
   `--tnzi-ai-viewport-inset-top` / `-bottom` are what of the host is above the
   screen and under the on-screen keyboard (written by the script; 0 without
   them). The host itself still covers the whole window, so the backdrop does
   too. Under the keyboard the home indicator is covered, so the bottom safe
   area only counts for what the keyboard does not already clear. -- */
.t-dialog-host--sheet,
.t-dialog-host--fullscreen {
  --t-dialog-safe-bottom: max(0px, calc(env(safe-area-inset-bottom, 0px) - var(--tnzi-ai-viewport-inset-bottom, 0px)));
}

/* -- A phone: a sheet across the bottom, the controls under the thumb. -- */
.t-dialog-host--sheet {
  align-items: flex-end;
  padding: max(var(--tnzi-overlay-inset-top, 0px), var(--tnzi-ai-viewport-inset-top, 0px)) 0
    var(--tnzi-ai-viewport-inset-bottom, 0px);
}
.t-dialog-host--sheet .t-dialog-host__panel {
  width: 100%;
  max-height: calc(100% - 32px);
  border-bottom: none;
  border-radius: var(--tnzi-ai-modal-radius, 20px) var(--tnzi-ai-modal-radius, 20px) 0 0;
  padding: 0 env(safe-area-inset-right, 0px) var(--t-dialog-safe-bottom) env(safe-area-inset-left, 0px);
}
.t-dialog-host--sheet .t-dialog-host__panel--sized {
  height: min(var(--t-dialog-height), calc(100% - 32px));
}

/* -- Full screen: the window below its top inset. Safe-area padding covers a
   notch only where the declared top inset does not already clear it. -- */
.t-dialog-host--fullscreen {
  align-items: stretch;
  justify-content: stretch;
  padding: max(var(--tnzi-overlay-inset-top, 0px), var(--tnzi-ai-viewport-inset-top, 0px)) 0
    var(--tnzi-ai-viewport-inset-bottom, 0px);
}
.t-dialog-host--fullscreen .t-dialog-host__panel,
.t-dialog-host--fullscreen .t-dialog-host__panel--sized {
  width: 100%;
  height: 100%;
  max-height: none;
  border: none;
  border-radius: 0;
  box-shadow: none;
  padding: max(0px, calc(env(safe-area-inset-top, 0px) - var(--tnzi-overlay-inset-top, 0px)))
    env(safe-area-inset-right, 0px) var(--t-dialog-safe-bottom) env(safe-area-inset-left, 0px);
}

/* -- Motion -- */
.t-dialog-fade-enter-active,
.t-dialog-fade-leave-active {
  transition: opacity var(--tnzi-ai-duration-base, 200ms) var(--tnzi-ai-easing, ease);
}
.t-dialog-fade-enter-active .t-dialog-host__panel,
.t-dialog-fade-leave-active .t-dialog-host__panel {
  transition: transform var(--tnzi-ai-duration-base, 200ms) var(--tnzi-ai-easing, ease);
}
.t-dialog-fade-enter-from,
.t-dialog-fade-leave-to {
  opacity: 0;
}
.t-dialog-fade-enter-from .t-dialog-host__panel,
.t-dialog-fade-leave-to .t-dialog-host__panel {
  transform: translateY(12px) scale(0.98);
}
.t-dialog-fade-enter-from.t-dialog-host--sheet .t-dialog-host__panel,
.t-dialog-fade-leave-to.t-dialog-host--sheet .t-dialog-host__panel {
  transform: translateY(24px);
}
@media (prefers-reduced-motion: reduce) {
  .t-dialog-fade-enter-active,
  .t-dialog-fade-leave-active,
  .t-dialog-fade-enter-active .t-dialog-host__panel,
  .t-dialog-fade-leave-active .t-dialog-host__panel {
    transition: none;
  }
}
</style>
