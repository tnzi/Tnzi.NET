<script setup lang="ts">
/**
 * TToastHost - draws the notes `useToast()` raises, stacked at the top centre
 * of the window on the toast layer (`--tnzi-ai-z-toast`). Mounted once per app:
 * `TChatApp` mounts it; an app with its own shell puts it beside its root.
 * A second host mounted for the same toaster draws nothing (see `Toaster.claim`).
 *
 * How many notes show at once follows the viewport: at most 3 on a wide
 * screen, 1 below `narrowBreakpoint` (768, `TChatApp`'s), where two stacked
 * notes would cover the page's header; past that the oldest makes room.
 * `narrow` forces either (`TChatApp` passes the signal its drawer uses).
 *
 * Accessibility, as the notes come and go without taking focus:
 * - the stack is a polite live region, and an error is `role="alert"`;
 * - a note never takes focus, so typing goes on where it was;
 * - its action buttons (up to two, e.g. "Undo" and "Send now") and its close
 *   button are reachable with Tab; while focus or the pointer is in a note, the
 *   note stays;
 * - pressing an action runs it and takes the note away, focus going back as
 *   after the close button; an action that keeps the note (`keepOpen`) leaves
 *   focus in it, on the close button when the action's own button is gone;
 * - a note at work (`busy`) shows a spinner and is `aria-busy`; a change of
 *   text marked `quiet` (a countdown's tick) is not announced;
 * - Escape on a focused note dismisses it, and focus goes back to where it was
 *   before it entered the stack (as it does after the close button). Escape
 *   anywhere else is not the stack's.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import {
  toaster as defaultToaster,
  TOAST_MAX_VISIBLE_NARROW,
  TOAST_MAX_VISIBLE_WIDE,
  type Toaster,
  type ToastAction,
  type ToastTone,
} from './toast'
import { useViewportBelow } from '../../headless/useViewportBelow'
import { useControlsText } from './controls-text'

const props = withDefaults(
  defineProps<{
    /** The toaster to show; the package-wide one (`useToast()`'s) by default. */
    toaster?: Toaster
    /** Viewport width (px) below which one note shows at a time. Default 768, as `TChatApp`. */
    narrowBreakpoint?: number
    /** Force the phone rule (`true`) or the wide one (`false`); unset follows `narrowBreakpoint`. */
    narrow?: boolean
  }>(),
  { toaster: undefined, narrowBreakpoint: 768, narrow: undefined },
)

const ICONS: Record<ToastTone, string> = {
  success: 'lucide:circle-check',
  info: 'lucide:info',
  warning: 'lucide:triangle-alert',
  error: 'lucide:circle-alert',
}

const source = computed(() => props.toaster ?? defaultToaster)
const self = Symbol('toast-host')
const text = useControlsText()
const stack = ref<HTMLElement | null>(null)

const isOwner = computed(() => source.value.owner.value === self)
const shown = computed(() => (isOwner.value ? source.value.items.value : []))

const viewportNarrow = useViewportBelow(() => props.narrowBreakpoint)
const isNarrow = computed(() => props.narrow ?? viewportNarrow.value)

/* The host that draws the toaster sets its limit; turning narrow drops the extra notes at once. */
watch(
  [isOwner, isNarrow, source],
  ([owner, narrow, toaster]) => {
    if (owner) toaster.setMaxVisible(narrow ? TOAST_MAX_VISIBLE_NARROW : TOAST_MAX_VISIBLE_WIDE)
  },
  { immediate: true },
)

/* Where focus was before it entered the stack: it goes back there when the
   note holding it leaves. */
let returnTo: HTMLElement | null = null

function onFocusIn(event: FocusEvent): void {
  const from = event.relatedTarget
  if (from instanceof HTMLElement && !stack.value?.contains(from)) returnTo = from
}

function focusIsInside(): boolean {
  const active = typeof document === 'undefined' ? null : document.activeElement
  return Boolean(active && stack.value?.contains(active))
}

function dismiss(id: number): void {
  const hadFocus = focusIsInside()
  source.value.dismiss(id)
  if (!hadFocus) return
  const target = returnTo
  returnTo = null
  void nextTick(() => {
    if (target?.isConnected) target.focus()
  })
}

/* The note leaves even when the action throws (the error goes on to the app),
   unless the action keeps it: then focus stays in the note, on its close button
   if the action's own button went away with the update it made. */
function act(id: number, action: ToastAction, event: MouseEvent): void {
  const note = (event.currentTarget as HTMLElement).closest('.t-toast')
  try {
    action.run()
  } finally {
    if (!action.keepOpen) dismiss(id)
    else {
      void nextTick(() => {
        if (!note?.isConnected) return
        const active = document.activeElement
        if (active && note.contains(active)) return
        note.querySelector<HTMLElement>('[data-toast-close]')?.focus()
      })
    }
  }
}

function onKeydown(event: KeyboardEvent, id: number): void {
  if (event.key !== 'Escape' || event.defaultPrevented) return
  event.preventDefault()
  event.stopPropagation()
  dismiss(id)
}

/* A note under the pointer or holding focus stays; leaving lets it run out. */
function onFocusOut(event: FocusEvent, id: number): void {
  const to = event.relatedTarget
  if (to instanceof Node && (event.currentTarget as HTMLElement).contains(to)) return
  source.value.resume(id)
}

onMounted(() => source.value.claim(self))
onBeforeUnmount(() => source.value.release(self))
</script>

<template>
  <Teleport to="body">
    <div ref="stack" class="t-toast-stack t-ai-scroll-scope" aria-live="polite" data-toast-stack @focusin="onFocusIn">
      <TransitionGroup name="t-toast">
        <div
          v-for="item in shown"
          :key="item.id"
          class="t-toast"
          :class="`t-toast--${item.tone}`"
          :role="item.tone === 'error' ? 'alert' : 'status'"
          :data-toast="item.tone"
          :aria-busy="item.busy || undefined"
          @mouseenter="source.pause(item.id)"
          @mouseleave="source.resume(item.id)"
          @focusin="source.pause(item.id)"
          @focusout="onFocusOut($event, item.id)"
          @keydown="onKeydown($event, item.id)"
        >
          <Icon v-if="item.busy" icon="lucide:loader-circle" class="t-toast__icon t-toast__spin" aria-hidden="true" />
          <Icon v-else :icon="ICONS[item.tone]" class="t-toast__icon" aria-hidden="true" />
          <span class="t-toast__text" :aria-live="item.quiet ? 'off' : undefined" data-toast-text>{{ item.text }}</span>
          <button
            v-for="(action, index) in item.actions"
            :key="`${index}:${action.label}`"
            type="button"
            class="t-toast__action"
            :data-toast-action="index"
            @click="act(item.id, action, $event)"
          >
            {{ action.label }}
          </button>
          <button type="button" class="t-toast__close" :aria-label="text.close" data-toast-close @click="dismiss(item.id)">
            <Icon icon="lucide:x" aria-hidden="true" />
          </button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<style scoped>
/* Below the page's top inset (a frameless window's title bar), centred. Only
   the notes take the pointer: the empty stack lets clicks through. */
.t-toast-stack {
  position: fixed;
  top: calc(12px + var(--tnzi-overlay-inset-top, 0px));
  left: 50%;
  z-index: var(--tnzi-ai-z-toast);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  width: max-content;
  max-width: calc(100vw - 24px);
  transform: translateX(-50%);
  pointer-events: none;
}
.t-toast {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  max-width: 480px;
  box-sizing: border-box;
  padding: 8px 6px 8px 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  box-shadow: var(--tnzi-ai-dialog-shadow);
  color: var(--tnzi-ai-text);
  font-size: 14px;
  line-height: 20px;
  pointer-events: auto;
}
.t-toast__icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 16px;
}
.t-toast--success .t-toast__icon {
  color: var(--tnzi-ai-success);
}
.t-toast--info .t-toast__icon {
  color: var(--tnzi-ai-info);
}
.t-toast--warning .t-toast__icon {
  color: var(--tnzi-ai-warning);
}
.t-toast--error .t-toast__icon {
  color: var(--tnzi-ai-danger);
}
.t-toast__text {
  min-width: 0;
  overflow-wrap: anywhere;
  white-space: pre-line;
}
.t-toast__action {
  flex-shrink: 0;
  align-self: center;
  height: 24px;
  margin: -2px 0;
  padding: 0 8px;
  border: 0;
  border-radius: var(--tnzi-ai-radius-sm, 6px);
  background: none;
  color: var(--tnzi-ai-accent-text);
  font-family: inherit;
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
  cursor: pointer;
}
.t-toast__action + .t-toast__action {
  margin-left: -6px;
}
/* Neutral whatever the tone: work in progress is not yet a success or an error. */
.t-toast .t-toast__spin {
  color: var(--tnzi-ai-text-secondary);
  animation: t-toast-spin 0.9s linear infinite;
}
@keyframes t-toast-spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-toast__spin {
    animation-duration: 2.4s;
  }
}
.t-toast__action:hover {
  background: var(--tnzi-ai-hover);
}
.t-toast__action:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
.t-toast__close {
  flex-shrink: 0;
  display: grid;
  place-items: center;
  width: 20px;
  height: 20px;
  padding: 0;
  border: 0;
  border-radius: var(--tnzi-ai-radius-sm, 6px);
  background: none;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 14px;
  cursor: pointer;
}
.t-toast__close:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-toast__close:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
.t-toast-enter-active,
.t-toast-leave-active {
  transition:
    opacity var(--tnzi-ai-duration-fast, 150ms) var(--tnzi-ai-easing, ease),
    transform var(--tnzi-ai-duration-fast, 150ms) var(--tnzi-ai-easing, ease);
}
.t-toast-enter-from,
.t-toast-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
@media (prefers-reduced-motion: reduce) {
  .t-toast-enter-active,
  .t-toast-leave-active {
    transition: none;
  }
}
</style>
