<script setup lang="ts">
/**
 * @experimental
 * TDockedDialog - a small window docked in a corner of the page that does not
 * block it: a mail being written, a note, a chat with support. The page
 * behind stays usable; the window can be folded to its title bar and opened
 * to a large size.
 *
 * Drawn from the shared dialog chrome (`dialog.css`: `.t-dialog`,
 * `.t-dialog__header` / `__title` / `__body` / `__footer`), so it reads as
 * one family with `TDialog`, with a compact title row: the title, then
 * minimise, maximise and close buttons.
 *
 * - **Non-modal**: `role="dialog"` without `aria-modal`, no backdrop, no
 *   focus trap, the page scrolls. It sits on the dialog layer's base
 *   (`--tnzi-ai-z-dialog`), under any modal dialog opened over the page and
 *   under menus and toasts.
 * - **Docked** at the bottom right (`corner="bottom-left"` for the other
 *   side), `width` × `height` (520 × 560 by default), kept inside the window.
 * - **Minimised** (`v-model:minimized`): only the title row, 300px wide; the
 *   body stays mounted, so what was typed is kept. The title row (or its
 *   restore button) brings it back. **Maximised** (`v-model:maximized`):
 *   centred, up to 960px wide and the window's height, below the page's top
 *   inset (`--tnzi-overlay-inset-top`).
 * - **On a phone** (below `narrowBreakpoint`, 768) it fills the screen below
 *   the top inset and then behaves as a modal layer (focus stays inside, the
 *   page does not scroll); minimised it is a bar across the bottom. Back
 *   closes it (`closeOnBack`), through `beforeClose`.
 *
 * Focus: opening moves focus into it (an `autofocus` / `data-autofocus`
 * element, else the first field, else the window, as `TDialog`); restoring it
 * from minimised does the same; minimising with focus inside puts focus on the
 * title row's restore button; closing with focus inside returns it to where it
 * was before the window opened. Escape closes it only with `closeOnEscape`
 * (off by default: a window someone is writing in is not a passing layer).
 * `beforeClose(reason)` runs before every close the window asks for, as in
 * `TDialog`, so a draft can be saved or the close refused.
 */
import { computed, nextTick, ref, useId, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useFocusTrap } from '@tnzi/ui/headless'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'
import { useViewportBelow } from '../../headless/useViewportBelow'
import { useBackLayer } from '../../headless/back-layers'
import { vTooltip } from '../../headless/tooltip'
import { closestOnPath, trackEventPaths } from '../../headless/event-path'
import { useAiI18n } from '../../i18n/index'
import { cssLength, dialogInitialFocus } from './dialog'
import type { DialogCloseReason } from './dialog-motion'
import { layerZIndex, useDialogLayer } from './layers'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    /** Accessible name when there is no `title`. */
    ariaLabel?: string
    corner?: 'bottom-right' | 'bottom-left'
    /** Docked width (number = px). */
    width?: number | string
    /** Docked height (number = px). */
    height?: number | string
    /** Show the minimise and maximise buttons. */
    resizable?: boolean
    closable?: boolean
    closeOnEscape?: boolean
    /** On a phone, back closes it rather than leaving the page. */
    closeOnBack?: boolean
    /** No padding around the body (an editor that draws its own). */
    flush?: boolean
    /** Viewport width (px) below which it fills the screen. Default 768. */
    narrowBreakpoint?: number
    narrow?: boolean
    beforeClose?: (reason: DialogCloseReason) => boolean | void | Promise<boolean | void>
  }>(),
  {
    title: undefined,
    ariaLabel: undefined,
    corner: 'bottom-right',
    width: 520,
    height: 560,
    resizable: true,
    closable: true,
    closeOnEscape: false,
    closeOnBack: true,
    flush: false,
    narrowBreakpoint: 768,
    narrow: undefined,
    beforeClose: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  /** After it has closed and its leave transition finished. */
  closed: []
}>()

const minimized = defineModel<boolean>('minimized', { default: false })
const maximized = defineModel<boolean>('maximized', { default: false })

defineOptions({ inheritAttrs: false })

defineSlots<{
  default?: (props: { close: () => Promise<boolean> }) => unknown
  footer?: (props: { close: () => Promise<boolean> }) => unknown
}>()

const t = useAiI18n()
trackEventPaths()
const titleId = `t-docked-title-${useId()}`
const panel = ref<HTMLElement | null>(null)
const restoreButton = ref<HTMLElement | null>(null)

const viewportNarrow = useViewportBelow(() => props.narrowBreakpoint)
const isNarrow = computed(() => props.narrow ?? viewportNarrow.value)
/* On a phone, open and not folded: it covers the page and acts as a modal layer. */
const coversPage = computed(() => props.modelValue && isNarrow.value && !minimized.value)

const state = computed(() => (minimized.value ? 'minimized' : isNarrow.value ? 'full' : maximized.value ? 'maximized' : 'docked'))

const modalZ = useDialogLayer(() => coversPage.value)
useBodyScrollLock(() => coversPage.value)

const panelStyle = computed(() => ({
  '--t-docked-width': cssLength(props.width) ?? '520px',
  '--t-docked-height': cssLength(props.height) ?? '560px',
  zIndex: coversPage.value ? modalZ.value : layerZIndex('dialog'),
}))


let pending: Promise<boolean> | null = null
function requestClose(reason: DialogCloseReason): Promise<boolean> {
  if (!props.modelValue) return Promise.resolve(false)
  if (pending) return pending
  if (!props.beforeClose) {
    emit('update:modelValue', false)
    return Promise.resolve(true)
  }
  pending = (async () => {
    try {
      if ((await props.beforeClose?.(reason)) === false) return false
      emit('update:modelValue', false)
      return true
    } finally {
      pending = null
    }
  })()
  return pending
}

const close = (): Promise<boolean> => requestClose('programmatic')

useFocusTrap(panel, () => coversPage.value, {
  onEscape: () => {
    if (props.closeOnEscape) void requestClose('escape')
  },
  initialFocus: () => (panel.value ? dialogInitialFocus(panel.value) : null),
})

/* Escape while docked: the window's own, only when asked for. On a phone the
   focus trap above takes it. */
function onKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || coversPage.value || !props.closeOnEscape || event.isComposing || event.defaultPrevented) return
  event.preventDefault()
  event.stopPropagation()
  void requestClose('escape')
}

useBackLayer(
  () => coversPage.value,
  () => (props.closable ? requestClose('back') : false),
  { enabled: () => props.closeOnBack },
)

/* Focus: into the window on open and on restore; out of a folded body to the
   restore button; back to where it was on close. */
let returnTo: HTMLElement | null = null

function focusInside(): boolean {
  const active = typeof document === 'undefined' ? null : document.activeElement
  return Boolean(active && panel.value?.contains(active))
}

function focusInitial(): void {
  void nextTick(() => {
    if (!panel.value || coversPage.value) return
    dialogInitialFocus(panel.value).focus({ preventScroll: true })
  })
}

watch(
  () => props.modelValue,
  (open, was) => {
    if (open && !was) {
      const active = typeof document === 'undefined' ? null : document.activeElement
      returnTo = active instanceof HTMLElement && active !== document.body ? active : null
      if (!minimized.value) focusInitial()
    } else if (!open && was) {
      const hadFocus = focusInside()
      const target = returnTo
      returnTo = null
      if (hadFocus && target) void nextTick(() => target.isConnected && target.focus())
    }
  },
  { immediate: true },
)

function toggleMinimized(): void {
  const folding = !minimized.value
  const hadFocus = focusInside()
  minimized.value = folding
  if (folding) {
    if (hadFocus) void nextTick(() => restoreButton.value?.focus())
  } else {
    focusInitial()
  }
}

function toggleMaximized(): void {
  if (minimized.value) minimized.value = false
  maximized.value = !maximized.value
}

/* A click on the title row of a folded window opens it again. */
function onHeaderClick(event: MouseEvent): void {
  if (!minimized.value) return
  /* Asked of the path the click took: the button's own handler has re-rendered
     it by now (a real pointer), so the icon under the pointer is detached. */
  if (closestOnPath(event, 'button', event.currentTarget as Element)) return
  toggleMinimized()
}

defineExpose({ close, panel })
</script>

<template>
  <Teleport to="body">
    <transition name="t-docked" @after-leave="emit('closed')">
      <section
        v-if="modelValue"
        ref="panel"
        v-bind="$attrs"
        class="t-dialog t-docked-dialog t-ai-scroll-scope"
        :class="[`t-docked-dialog--${state}`, `t-docked-dialog--${corner}`, { 't-dialog--narrow': isNarrow }]"
        :style="panelStyle"
        role="dialog"
        :aria-labelledby="title ? titleId : undefined"
        :aria-label="title ? undefined : ariaLabel"
        tabindex="-1"
        data-docked-dialog
        :data-state="state"
        @keydown="onKeydown"
      >
        <header class="t-docked-dialog__header" @click="onHeaderClick">
          <h2 :id="titleId" class="t-docked-dialog__title">{{ title }}</h2>
          <button
            v-if="resizable || minimized"
            ref="restoreButton"
            v-tooltip="minimized ? t.common.restore : t.common.minimize"
            type="button"
            class="t-docked-dialog__button"
            :aria-label="minimized ? t.common.restore : t.common.minimize"
            :aria-expanded="!minimized"
            data-docked-minimize
            @click="toggleMinimized"
          >
            <Icon :icon="minimized ? 'lucide:chevron-up' : 'lucide:minus'" aria-hidden="true" />
          </button>
          <button
            v-if="resizable && !isNarrow"
            v-tooltip="maximized && !minimized ? t.common.restore : t.common.maximize"
            type="button"
            class="t-docked-dialog__button"
            :aria-label="maximized && !minimized ? t.common.restore : t.common.maximize"
            :aria-pressed="maximized && !minimized"
            data-docked-maximize
            @click="toggleMaximized"
          >
            <Icon :icon="maximized && !minimized ? 'lucide:minimize-2' : 'lucide:maximize-2'" aria-hidden="true" />
          </button>
          <button
            v-if="closable"
            v-tooltip="t.common.close"
            type="button"
            class="t-docked-dialog__button"
            :aria-label="t.common.close"
            data-docked-close
            @click="requestClose('close-button')"
          >
            <Icon icon="lucide:x" aria-hidden="true" />
          </button>
        </header>
        <div v-show="!minimized" class="t-dialog__body t-ai-scroll t-docked-dialog__body" :class="{ 't-dialog__body--flush': flush }">
          <slot :close="close" />
        </div>
        <footer v-if="$slots.footer" v-show="!minimized" class="t-dialog__footer t-docked-dialog__footer">
          <slot name="footer" :close="close" />
        </footer>
      </section>
    </transition>
  </Teleport>
</template>

<style scoped>
/* The shared panel, docked: fixed in a corner, a smaller radius, no
   backdrop. Sizes never exceed the window. */
.t-docked-dialog {
  --t-docked-edge: 16px;
  --t-dialog-pad: 16px;
  position: fixed;
  bottom: var(--t-docked-edge);
  width: min(var(--t-docked-width), calc(100vw - 2 * var(--t-docked-edge)));
  height: min(var(--t-docked-height), calc(100dvh - var(--tnzi-overlay-inset-top, 0px) - 2 * var(--t-docked-edge)));
  border-radius: 14px;
}
.t-docked-dialog--bottom-right {
  right: var(--t-docked-edge);
}
.t-docked-dialog--bottom-left {
  left: var(--t-docked-edge);
}
.t-docked-dialog--minimized {
  width: min(300px, calc(100vw - 2 * var(--t-docked-edge)));
  height: auto;
}
.t-docked-dialog--maximized {
  top: calc(var(--tnzi-overlay-inset-top, 0px) + 24px);
  bottom: 24px;
  left: 50%;
  right: auto;
  width: min(960px, calc(100vw - 48px));
  height: auto;
  transform: translateX(-50%);
}
/* A phone: the screen below the top inset; folded, a bar across the bottom. */
.t-docked-dialog--full {
  top: var(--tnzi-overlay-inset-top, 0px);
  right: 0;
  bottom: 0;
  left: 0;
  width: auto;
  height: auto;
  border: none;
  border-radius: 0;
  box-shadow: none;
  padding: 0 env(safe-area-inset-right, 0px) env(safe-area-inset-bottom, 0px) env(safe-area-inset-left, 0px);
}
.t-dialog--narrow.t-docked-dialog--minimized {
  right: 0;
  left: 0;
  bottom: 0;
  width: auto;
  border-radius: 14px 14px 0 0;
  padding-bottom: env(safe-area-inset-bottom, 0px);
}

.t-docked-dialog__header {
  flex: none;
  display: flex;
  align-items: center;
  gap: 2px;
  height: 44px;
  padding: 0 8px 0 var(--t-dialog-pad);
  border-bottom: 1px solid var(--tnzi-ai-divider);
}
.t-docked-dialog--minimized .t-docked-dialog__header {
  border-bottom-color: transparent;
  cursor: pointer;
}
.t-docked-dialog__title {
  flex: 1;
  min-width: 0;
  margin: 0 8px 0 0;
  overflow: hidden;
  color: var(--tnzi-ai-text);
  font-size: 14px;
  font-weight: 600;
  line-height: 20px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-docked-dialog__button {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-size: 16px;
  cursor: pointer;
}
.t-dialog--narrow .t-docked-dialog__button {
  width: 36px;
  height: 36px;
  font-size: 18px;
}
.t-docked-dialog__button:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-docked-dialog__button:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
.t-docked-dialog__body {
  padding-top: var(--t-dialog-pad);
}
.t-docked-dialog__body.t-dialog__body--flush {
  padding: 0;
}

.t-docked-enter-active,
.t-docked-leave-active {
  transition:
    opacity var(--tnzi-ai-duration-base, 200ms) var(--tnzi-ai-easing, ease),
    translate var(--tnzi-ai-duration-base, 200ms) var(--tnzi-ai-easing, ease);
}
.t-docked-enter-from,
.t-docked-leave-to {
  opacity: 0;
  translate: 0 16px;
}
@media (prefers-reduced-motion: reduce) {
  .t-docked-enter-active,
  .t-docked-leave-active {
    transition: none;
  }
}
</style>
