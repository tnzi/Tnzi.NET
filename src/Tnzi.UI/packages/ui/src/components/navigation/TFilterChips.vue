<template>
  <div class="t-filter-chips" role="group" :aria-label="ariaLabel">
    <button
      v-for="option in options"
      :key="option.key"
      type="button"
      class="t-filter-chips__chip"
      :class="{ 't-filter-chips__chip--active': isActive(option) }"
      :aria-pressed="isActive(option)"
      :disabled="option.disabled"
      @click="select(option)"
    >
      <span
        v-if="option.color"
        class="t-filter-chips__dot"
        :style="{ background: option.color }"
        aria-hidden="true"
      />
      <span class="t-filter-chips__label">{{ option.label }}</span>
      <span v-if="option.count != null" class="t-filter-chips__count">{{ option.count }}</span>
    </button>
  </div>
</template>

<script setup lang="ts" generic="TKey extends FilterChipKey">
/**
 * `TFilterChips` - a wrapping row of single-select filter chips, each with an
 * optional live count, sitting above the list it filters.
 *
 * ```vue
 * <TFilterChips v-model="docType" :options="docTypeOptions" aria-label="Filter by type" />
 * ```
 * ```ts
 * const docTypeOptions = computed<FilterChipOption<DocType | 'all'>[]>(() => [
 *   { key: 'all', label: 'All', count: active.value.length },
 *   { key: 'contract', label: 'Contracts', count: byType.value.contract, color: '#2f80ed' },
 * ])
 * ```
 *
 * ## The caller owns the options, the counts, and "All"
 *
 * The component never derives a count and never invents an "All" chip: every
 * chip - including "All" - is an entry the caller passes. Counts are genuinely
 * not "length of the filtered array" at real call sites (one of the three this
 * replaces excludes withdrawn rows from its All count on purpose), so a chip bar
 * that computed them would be wrong for the very screens it exists to serve.
 *
 * ## Clearing
 *
 * By default, clicking the selected chip does nothing - the bar always has
 * exactly one selection. `clearable` makes that click emit `null` instead, for
 * bars whose neutral state is "no filter" rather than an "All" entry. Opt-in
 * rather than always-on: a filter that can be silently cleared by a second click
 * is a surprise on a bar that already offers "All" as the way back.
 *
 * ## Selected state
 *
 * Tinted primary background + primary text + primary border, never a solid fill
 * with white text. A filter bar is a secondary control above the list; five or
 * six solid chips become a wall of colour that pulls the eye off the list, which
 * is the thing on the page that actually matters. The tint is derived from
 * `--tnzi-primary-rgb`, so it follows whatever primary the consuming app
 * configures instead of pinning a colour.
 */

/** What a chip's identity may be. Numeric keys keep backend enums usable as-is. */
export type FilterChipKey = string | number

export interface FilterChipOption<TOptionKey extends FilterChipKey = string> {
  /** Stable identity - this is what `v-model` carries. */
  key: TOptionKey
  /** Chip text. Already translated: the component owns no message catalogue. */
  label: string
  /**
   * Live count shown after the label. Omit it and the chip renders label-only;
   * `0` is a count and renders as `0` (a category with nothing in it is not the
   * same statement as a category that does not report a number).
   */
  count?: number
  /**
   * Leading colour dot - any CSS colour. For bars whose categories already carry
   * a colour elsewhere on the page (a type legend, a status swatch); leave it
   * unset and no dot is rendered.
   */
  color?: string
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{
    /** Chips in render order. */
    options: FilterChipOption<TKey>[]
    /** Selected key; `null` / omitted means nothing is selected. */
    modelValue?: TKey | null
    /**
     * Clicking the selected chip clears the selection (emits `null`).
     * Default false - the selected chip is inert.
     */
    clearable?: boolean
    /** Accessible name of the chip group. */
    ariaLabel?: string
  }>(),
  { modelValue: null, clearable: false, ariaLabel: 'Filter' },
)

const emit = defineEmits<{
  'update:modelValue': [value: TKey | null]
}>()

const isActive = (option: FilterChipOption<TKey>): boolean => option.key === props.modelValue

// `option.disabled` needs no guard here: the chip is a native `<button>`, which
// dispatches no click at all while disabled. A mirrored JS check would be a
// branch nothing can ever reach - the reason to render real buttons rather than
// `role="button"` divs is precisely that the platform enforces this for us.
function select(option: FilterChipOption<TKey>): void {
  if (isActive(option)) {
    if (props.clearable) emit('update:modelValue', null)
    return
  }
  emit('update:modelValue', option.key)
}
</script>

<style scoped>
.t-filter-chips {
  display: flex;
  /* Wraps onto a second and third line rather than scrolling or overflowing:
     a six-chip bar has to survive a 390px viewport, and a horizontal scroller
     hides filters the user has no reason to suspect exist. */
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.t-filter-chips__chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  /* Together with the label's ellipsis, keeps one long label from pushing the
     row wider than the viewport. */
  max-width: 100%;
  padding: 3px 10px;
  border: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.09));
  border-radius: var(--tnzi-radius-pill, 999px);
  background: transparent;
  color: var(--tnzi-base-text, currentColor);
  /* Buttons do not inherit the page font. */
  font-family: inherit;
  font-size: 12px;
  line-height: 20px;
  /* Constant across states on purpose: bolding the selected chip would resize
     it and shift every chip after it sideways on each click. */
  font-weight: 500;
  cursor: pointer;
  transition:
    background var(--tnzi-duration-fast, 120ms) var(--tnzi-easing, ease),
    border-color var(--tnzi-duration-fast, 120ms) var(--tnzi-easing, ease),
    color var(--tnzi-duration-fast, 120ms) var(--tnzi-easing, ease);
}

.t-filter-chips__chip:hover:not(:disabled) {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.05);
}

.t-filter-chips__chip--active,
.t-filter-chips__chip--active:hover:not(:disabled) {
  background: rgb(var(--tnzi-primary-rgb, 32 128 240) / 0.12);
  border-color: rgb(var(--tnzi-primary-rgb, 32 128 240) / 0.45);
  color: var(--tnzi-primary, #2080f0);
}

.t-filter-chips__chip:focus-visible {
  outline: 2px solid var(--tnzi-primary, #2080f0);
  outline-offset: 2px;
}

.t-filter-chips__chip:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.t-filter-chips__dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.t-filter-chips__label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-filter-chips__count {
  flex: none;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.45));
}

.t-filter-chips__chip--active .t-filter-chips__count {
  /* Inside a tinted chip the muted grey reads as disabled; ride the chip's own
     primary at reduced weight instead. */
  color: inherit;
  opacity: 0.75;
}
</style>
