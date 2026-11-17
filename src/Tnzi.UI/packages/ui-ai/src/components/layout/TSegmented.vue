<script setup lang="ts" generic="T extends string">
/**
 * @experimental
 * TSegmented - a few options side by side, one chosen: the tabs at the start
 * of a page toolbar ("Yours / Runs / Templates").
 *
 * A radio group (`role="radiogroup"`): the arrow keys move the choice, Tab
 * enters and leaves the group at the chosen option. On a phone the group does
 * not wrap; it scrolls sideways inside the toolbar row instead, and the chosen
 * option is scrolled into view when it mounts and whenever the choice changes
 * (only sideways, inside the scroller: the page itself does not move).
 *
 * One size whatever it holds: the group is as tall as the toolbar's controls
 * (`--tnzi-ai-toolbar-control-size`, 30px), with 2px around options 4px
 * shorter (26px), so it lines up with the search and icon buttons beside it.
 *
 * The icons are 16px, or `--tnzi-ai-segmented-icon-size` set on the group or
 * on an element around it. The options follow a larger icon: at least 4px
 * above and below it (a 20px icon makes options 28px tall and the group 32px),
 * an icon-only option at least 7px either side of it (34px wide at 20px), and
 * an icon-only option's dot and running ring stay on the icon's corner.
 *
 * Two shapes besides the default (icon and label, each option as wide as its
 * text):
 * - `iconOnly`: each option shows its icon alone, square-ish (32px wide); the
 *   label is the option's accessible name and its tooltip. An option without
 *   an icon still shows its label.
 * - `block`: the group fills its container and the options share the width
 *   evenly; a label too long for its share is cut with an ellipsis.
 * - `grow`: the group fills its container and each option grows from its own
 *   width, so a longer label keeps more room (a row of efforts on a phone,
 *   "Low / Medium / Extra high"); a label is cut only when the row cannot hold
 *   them all.
 *
 * A label longer than 13em (an email address naming a mailbox) is cut with an
 * ellipsis, so one long option cannot push the others out of the row; its
 * tooltip then shows it whole.
 *
 * An option's `badge` is a count at its right (how many things there wait on
 * the person), shown only when above 0, with `badgeLabel` the words a screen
 * reader hears for it. An option's `dot` is a small warning (or `'danger'`)
 * dot instead, for something there that needs a look with no count to give
 * (a mailbox whose sync failed), or with `'muted'` a quiet mark that only
 * points the option out (the default among several), with `dotLabel` its screen reader words; on
 * an icon-only option it sits on the icon's corner. An option's `running` says
 * something there is going on now (a turn of a conversation in another mode): a
 * small turning ring in the dot's place, which circles the dot when there is one
 * too, so "needs a look" and "still moving" read together; it stands still under
 * `prefers-reduced-motion`, and `runningLabel` (default the locale's "Working") is
 * what a screen reader hears. On an icon-only option the badge, dot and running
 * words join the label in the button's accessible name. An option's `attrs` go on its button, before the
 * component's own, so `role` and `aria-checked` cannot be overridden.
 *
 * The chosen option's fill is a thumb of its own under the options, which
 * slides (place and width) from the old choice to the new one whenever the
 * choice changes. It stands where it belongs at once on the first render, when
 * an option changes size (a font arriving, the container growing, a label or
 * badge changing) or the options change, and always under
 * `prefers-reduced-motion`. It scrolls with the options, since it lies among
 * them.
 */
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useControlsText } from '../controls/controls-text'
import { vTooltip } from '../../headless/tooltip'
import { scrollLeftToReveal, type SegmentedOption } from './page-toolbar'

const props = defineProps<{
  modelValue: T
  options: ReadonlyArray<SegmentedOption<T>>
  /** Names the group for screen readers. */
  label: string
  disabled?: boolean
  /** Icons alone; each label becomes its option's accessible name and tooltip. */
  iconOnly?: boolean
  /** Fill the container's width, the options sharing it evenly. */
  block?: boolean
  /** Fill the container's width, each option growing from its own width. */
  grow?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: T] }>()

const root = ref<HTMLElement | null>(null)
const thumb = ref<HTMLElement | null>(null)
const text = useControlsText()

const hasBadge = (option: SegmentedOption<T>) => !!option.badge && option.badge > 0
const hasDot = (option: SegmentedOption<T>) => !hasBadge(option) && !!option.dot
const runningWords = (option: SegmentedOption<T>) => option.runningLabel || text.value.running

/* An icon alone is named by its label; what the option carries joins it there,
   since the words inside a button named by `aria-label` are not read. */
function iconName(option: SegmentedOption<T>): string {
  return [
    option.label,
    hasBadge(option) ? option.badgeLabel : undefined,
    hasDot(option) ? option.dotLabel : undefined,
    option.running ? runningWords(option) : undefined,
  ].filter(Boolean).join(', ')
}

/* The nearest box that scrolls sideways: the group itself, or the toolbar's
   start that holds it. */
function horizontalScroller(from: HTMLElement): HTMLElement | null {
  for (let node: HTMLElement | null = from; node; node = node.parentElement) {
    const overflowX = getComputedStyle(node).overflowX
    if ((overflowX === 'auto' || overflowX === 'scroll') && node.scrollWidth > node.clientWidth) return node
  }
  return null
}

function revealChosen(smooth: boolean): void {
  const group = root.value
  const chosen = group?.querySelector<HTMLElement>('.t-segmented__option.is-selected')
  if (!group || !chosen) return
  const scroller = horizontalScroller(group)
  if (!scroller) return
  const left = scrollLeftToReveal(scroller.getBoundingClientRect(), chosen.getBoundingClientRect(), scroller.scrollLeft)
  if (left === null) return
  const reduce = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches
  if (typeof scroller.scrollTo === 'function') scroller.scrollTo({ left, behavior: smooth && !reduce ? 'smooth' : 'auto' })
  else scroller.scrollLeft = left
}

const prefersReducedMotion = () =>
  typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches

/* The thumb is placed by hand rather than through a binding: whether a move
   slides depends on what moved it, and a move that must not slide has to
   reach the page with its transition off before the transition comes back.
   `data-sliding` is what turns the transition on (see the stylesheet). */
function placeThumb(slide: boolean): void {
  const group = root.value
  const fill = thumb.value
  if (!group || !fill) return
  const chosen = group.querySelector<HTMLElement>('.t-segmented__option.is-selected')
  if (!chosen) {
    fill.style.display = 'none'
    return
  }
  const wasHidden = fill.style.display === 'none'
  const animate = slide && !wasHidden && !prefersReducedMotion()
  if (animate) fill.setAttribute('data-sliding', '')
  else fill.removeAttribute('data-sliding')
  /* Measured from the group's padding box (where an absolute box starts), as
     laid out: the group's own sideways scroll is added back, and fractions
     are kept, which `offsetLeft` / `offsetWidth` would round. */
  const box = group.getBoundingClientRect()
  const rect = chosen.getBoundingClientRect()
  const x = rect.left - box.left - group.clientLeft + group.scrollLeft
  const y = rect.top - box.top - group.clientTop + group.scrollTop
  fill.style.display = ''
  fill.style.width = `${rect.width}px`
  fill.style.height = `${rect.height}px`
  fill.style.transform = `translate(${x}px, ${y}px)`
  /* Without a transition, the place has to be laid out now: otherwise the
     next slide would start from wherever the page last drew the thumb. */
  if (!animate) void fill.offsetWidth
}

/* An option changing size moves the thumb's target without a choice being
   made: follow it at once. */
let resizes: ResizeObserver | null = null
function observeOptions(): void {
  resizes?.disconnect()
  if (typeof ResizeObserver !== 'function' || !root.value) return
  resizes ??= new ResizeObserver(() => placeThumb(false))
  resizes.observe(root.value)
  root.value.querySelectorAll<HTMLElement>('.t-segmented__option').forEach((option) => resizes!.observe(option))
}

onMounted(() => {
  placeThumb(false)
  observeOptions()
  void nextTick(() => revealChosen(false))
})
onBeforeUnmount(() => {
  resizes?.disconnect()
  resizes = null
})
watch(
  () => props.modelValue,
  () => {
    placeThumb(true)
    void nextTick(() => revealChosen(true))
  },
  { flush: 'post' },
)
watch(
  () => props.options.map((o) => `${o.value}\u0000${o.label}`).join('\u0001'),
  () => {
    observeOptions()
    placeThumb(false)
  },
  { flush: 'post' },
)

function move(from: HTMLElement, step: number): void {
  const enabled = props.options.filter((o) => !o.disabled && !props.disabled)
  if (enabled.length === 0) return
  const index = enabled.findIndex((o) => o.value === props.modelValue)
  const next = enabled[(index + step + enabled.length) % enabled.length]!
  emit('update:modelValue', next.value)
  const group = from.closest('.t-segmented')
  requestAnimationFrame(() => group?.querySelector<HTMLElement>(`[data-value="${CSS.escape(next.value)}"]`)?.focus())
}

function onKeydown(event: KeyboardEvent): void {
  const target = event.target as HTMLElement
  if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
    event.preventDefault()
    move(target, 1)
  } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
    event.preventDefault()
    move(target, -1)
  }
}
</script>

<template>
  <div
    ref="root"
    class="t-segmented"
    :class="{ 't-segmented--icon-only': iconOnly, 't-segmented--block': block, 't-segmented--grow': grow }"
    role="radiogroup"
    :aria-label="label"
    @keydown="onKeydown"
  >
    <!-- The chosen option's fill, under the options; placed by placeThumb. -->
    <span
      ref="thumb"
      class="t-segmented__thumb"
      :data-disabled="disabled || options.find((o) => o.value === modelValue)?.disabled ? '' : undefined"
      aria-hidden="true"
      data-segment-thumb
    />
    <button
      v-for="option in options"
      :key="option.value"
      v-bind="option.attrs"
      v-tooltip="iconOnly && option.icon ? option.label : { text: option.label, whenTruncated: '.t-segmented__label' }"
      type="button"
      role="radio"
      class="t-segmented__option"
      :class="{ 'is-selected': option.value === modelValue, 'is-icon': iconOnly && !!option.icon }"
      :aria-checked="option.value === modelValue"
      :aria-label="iconOnly && option.icon ? iconName(option) : undefined"
      :tabindex="option.value === modelValue ? 0 : -1"
      :disabled="disabled || option.disabled"
      :data-value="option.value"
      @click="emit('update:modelValue', option.value)"
    >
      <Icon v-if="option.icon" :icon="option.icon" aria-hidden="true" />
      <span v-if="!iconOnly || !option.icon" class="t-segmented__label">{{ option.label }}</span>
      <span v-if="hasBadge(option)" class="t-segmented__badge" data-segment-badge>
        <span aria-hidden="true">{{ option.badge }}</span>
        <span v-if="option.badgeLabel" class="sr-only">{{ option.badgeLabel }}</span>
      </span>
      <span
        v-else-if="option.dot"
        class="t-segmented__dot"
        :class="{
          't-segmented__dot--danger': option.dot === 'danger',
          't-segmented__dot--muted': option.dot === 'muted',
          'is-running': option.running,
        }"
        data-segment-dot
      >
        <span v-if="option.dotLabel" class="sr-only">{{ option.dotLabel }}</span>
      </span>
      <!-- Going on: a ring of its own, or (with a dot) the ring around it, drawn
           by the dot itself so the two stay centred on each other. -->
      <span
        v-if="option.running"
        class="t-segmented__running"
        :class="{ 'is-around-dot': hasDot(option) }"
        data-segment-running
      >
        <span class="sr-only">{{ runningWords(option) }}</span>
      </span>
    </button>
  </div>
</template>

<style scoped>
/* Total height = the toolbar's control size: 2px padding around options 4px
   shorter than it (30 -> 26px), unless the icon needs more (4px above and below
   it). Radii nest: 9px outside, 7px inside (9 - 2). */
.t-segmented {
  --t-segmented-pad: 2px;
  --t-segmented-icon: var(--tnzi-ai-segmented-icon-size, 16px);
  position: relative;
  display: inline-flex;
  flex: none;
  gap: 2px;
  padding: var(--t-segmented-pad);
  border-radius: 9px;
  background: var(--tnzi-ai-hover);
}
/* The chosen option's fill: placed under it by the script (place and size),
   sliding only while `data-sliding` is on. */
.t-segmented__thumb {
  position: absolute;
  top: 0;
  left: 0;
  box-sizing: border-box;
  border-radius: 7px;
  background: var(--tnzi-ai-surface);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
  pointer-events: none;
}
.t-segmented__thumb[data-sliding] {
  transition: transform var(--tnzi-ai-duration-base) var(--tnzi-ai-easing),
    width var(--tnzi-ai-duration-base) var(--tnzi-ai-easing);
}
/* As the chosen option itself would be. */
.t-segmented__thumb[data-disabled] {
  opacity: 0.5;
}
/* Over the thumb: positioned and later in the group, so drawn after it. */
.t-segmented__option {
  position: relative;
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  gap: 6px;
  height: max(
    calc(var(--tnzi-ai-toolbar-control-size, 30px) - 2 * var(--t-segmented-pad)),
    calc(var(--t-segmented-icon) + 8px)
  );
  padding: 0 10px;
  border: none;
  border-radius: 7px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-family: inherit;
  font-size: 13px;
  /* Its own, not the page's: a button's line height is whatever the page's
     reset says (1.15 in most, `inherit` under a utility preflight), and the
     label would sit a pixel higher or lower from one page to the next. */
  line-height: 1.5;
  white-space: nowrap;
  cursor: pointer;
  transition: color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
/* 32px, or 7px either side of a larger icon (34px at 20px). */
.t-segmented__option.is-icon {
  width: max(32px, calc(var(--t-segmented-icon) + 14px));
  padding: 0;
}
/* A long label is cut (its tooltip shows it whole). */
.t-segmented__label {
  min-width: 0;
  max-width: 13em;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* Fills its container; the options split it evenly (a zero basis, so a long
   label does not take a bigger share) and cut a label that does not fit. */
.t-segmented--block {
  display: flex;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}
.t-segmented--block .t-segmented__option {
  flex: 1 1 0;
  min-width: 0;
  width: auto;
}
.t-segmented--grow {
  display: flex;
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}
.t-segmented--grow .t-segmented__option {
  flex: 1 1 auto;
  min-width: 0;
  padding: 0 6px;
}
.t-segmented--block .t-segmented__label {
  min-width: 0;
  max-width: none;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* The count of what waits in an option. `position: relative` holds its screen
   reader words, which would otherwise widen the scrolling box around it. */
.t-segmented__badge {
  position: relative;
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  box-sizing: border-box;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  background: var(--tnzi-ai-warning);
  color: var(--tnzi-ai-on-status);
  font-size: 10.5px;
  font-weight: 600;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
/* Something there needs a look, with no count: a 6px dot after the label, or
   on the icon's top right corner when the option is an icon alone (its top on
   the icon's top, 1px past its right edge; the icon is centred, so its edges
   are half its size from the option's middle). */
.t-segmented__dot {
  position: relative;
  flex: none;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--tnzi-ai-warning);
}
.t-segmented__dot--danger {
  background: var(--tnzi-ai-danger);
}
.t-segmented__dot--muted {
  width: 4px;
  height: 4px;
  background: var(--tnzi-ai-text-tertiary);
}
.t-segmented__option.is-icon .t-segmented__dot {
  position: absolute;
  top: calc(50% - var(--t-segmented-icon) / 2);
  right: calc(50% - var(--t-segmented-icon) / 2 - 1px);
  box-shadow: 0 0 0 1.5px var(--tnzi-ai-hover);
}
.t-segmented__option.is-icon.is-selected .t-segmented__dot {
  box-shadow: 0 0 0 1.5px var(--tnzi-ai-surface);
}
/* Going on: a 10px ring, thin, in the accent, a quarter of it lit and turning.
   After the label like the dot; on an icon alone, on the icon's corner (2px
   above its top, 3px past its right edge). With a
   dot there the ring is drawn around the dot instead (the dot's own ::after),
   and this element only carries the words. */
.t-segmented__running {
  position: relative;
  flex: none;
  width: 10px;
  height: 10px;
  box-sizing: border-box;
  border-radius: 50%;
  border: 1.5px solid color-mix(in srgb, var(--tnzi-ai-accent) 30%, transparent);
  border-top-color: var(--tnzi-ai-accent);
  animation: t-segmented-turn 0.9s linear infinite;
}
.t-segmented__running.is-around-dot {
  width: 0;
  height: 0;
  border: none;
  animation: none;
  margin-left: -6px;
}
.t-segmented__option.is-icon .t-segmented__running {
  position: absolute;
  top: calc(50% - var(--t-segmented-icon) / 2 - 2px);
  right: calc(50% - var(--t-segmented-icon) / 2 - 3px);
}
/* Over the icon: the group's fill around it keeps the ring off the glyph. */
.t-segmented__option.is-icon .t-segmented__running:not(.is-around-dot) {
  background: var(--tnzi-ai-hover);
}
.t-segmented__option.is-icon.is-selected .t-segmented__running:not(.is-around-dot) {
  background: var(--tnzi-ai-surface);
}
.t-segmented__dot.is-running::after {
  content: '';
  position: absolute;
  inset: -3px;
  border-radius: 50%;
  border: 1.5px solid transparent;
  border-top-color: var(--tnzi-ai-accent);
  border-right-color: var(--tnzi-ai-accent);
  animation: t-segmented-turn 0.9s linear infinite;
}
@keyframes t-segmented-turn { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) {
  .t-segmented__running,
  .t-segmented__dot.is-running::after {
    animation: none;
  }
}
.t-segmented__option:hover:not(:disabled):not(.is-selected) {
  color: var(--tnzi-ai-text);
}
.t-segmented__option.is-selected {
  color: var(--tnzi-ai-text);
}
/* On the dark canvas the surface is barely lighter than the group: the choice
   gets a ring too. */
:root.dark .t-segmented__thumb,
:root[data-theme='dark'] .t-segmented__thumb {
  background: var(--tnzi-ai-selected);
  box-shadow: inset 0 0 0 1px var(--tnzi-ai-border-strong);
}
.t-segmented__option:disabled {
  opacity: 0.5;
  cursor: default;
}
/* No offset: the 2px ring then fits in the group's 2px padding, which a
   toolbar scrolling sideways on a phone would otherwise clip. */
.t-segmented__option:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 0;
}
.t-segmented__option :deep(svg) {
  flex: none;
  width: var(--t-segmented-icon);
  height: var(--t-segmented-icon);
}
@media (prefers-reduced-motion: reduce) {
  .t-segmented__option,
  .t-segmented__thumb[data-sliding] {
    transition: none;
  }
}
</style>
