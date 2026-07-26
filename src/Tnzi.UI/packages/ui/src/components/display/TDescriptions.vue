<template>
  <dl class="t-desc" :class="[`t-desc--${layout}`, { 't-desc--bordered': bordered }]" :style="gridStyle">
    <div
      v-for="(item, index) in visibleItems"
      :key="item.key ?? `${item.label}-${index}`"
      class="t-desc__row"
      :style="rowStyle(item)"
    >
      <dt class="t-desc__label" :style="labelStyle">
        <slot name="label" :item="item">{{ resolveLabel(item) }}</slot>
      </dt>
      <dd class="t-desc__value" :class="{ 't-desc__value--empty': isBlank(item) }">
        <slot name="value" :item="item">
          <TDescriptionValue :item="item" />
        </slot>
      </dd>
    </div>
  </dl>
</template>

<script setup lang="ts">
/**
 * TDescriptions - the read-only counterpart of a form.
 *
 * A record's fields shown as `label: value` pairs in a responsive grid, so a
 * detail page / view drawer stops rendering a column of greyed-out disabled
 * inputs (which reads as "a database row someone switched off") and starts
 * reading as a document.
 *
 * Two layouts:
 *   - `inline` (default) - label left at a fixed width, value right. Dense,
 *     scans well for records with short values (ids, codes, dates, amounts).
 *   - `stack` - label above value. Better when values are long (addresses,
 *     descriptions) or when the container is narrow.
 *
 * Column count follows the CONTAINER, not the viewport: the grid uses
 * `repeat(auto-fit, minmax(<minWidth>, 1fr))`, so the same component renders 3
 * columns on a wide detail panel and 1 column inside a 420px drawer with no
 * breakpoint wiring at the call site.
 *
 * A field with nothing to show renders `EMPTY_DASH` in a muted tone - never a
 * blank cell (which is indistinguishable from a rendering bug) and never `0`.
 *
 * (Doc comment in the script, not above the root element: a leading comment
 * node in `<template>` makes the component multi-root and breaks fallthrough.)
 */
import { computed, h, type CSSProperties, type FunctionalComponent, type VNodeChild } from 'vue'
import { EMPTY_DASH, isEmptyValue } from '../../utils/placeholders'

export interface DescriptionItem {
  /** Stable key (used for the v-for key and slot addressing). */
  key?: string
  /** Label text, or a fallback when `labelKey` misses. */
  label: string
  /** i18n key resolved through the `translate` prop. Wins over `label`. */
  labelKey?: string
  /** Raw value. Rendered via `String(value)`; blank values become a dash. */
  value?: unknown
  /** Full control over the value cell (badges, links, money, chips). */
  render?: () => VNodeChild
  /**
   * How many grid columns the row spans. `'full'` (or any number ≥ 2) claims
   * the whole row - the column count is container-derived, so a numeric span
   * cannot be honoured exactly without risking overflow at 1 column.
   */
  span?: number | 'full'
  /** Skip this row entirely (e.g. a field that only applies to one record kind). */
  hidden?: boolean
}

interface Props {
  items: DescriptionItem[]
  /**
   * Minimum width one column may shrink to before the grid drops a column.
   * The default suits short values; raise it for long text so the layout
   * folds to fewer, wider columns.
   */
  minColumnWidth?: number
  /**
   * Hard cap on the column count regardless of available width.
   *
   * `1` renders a genuine single column that still fills its container. It does
   * NOT narrow the block: an earlier implementation enforced the cap with a
   * `max-width`, which pinned every single-column read-only record to 280px and
   * left the rest of the row blank (worst on phones, where `resolveCols()`
   * collapses any multi-column form to 1).
   */
  maxColumns?: number
  /** `inline` = label left / value right; `stack` = label above value. */
  layout?: 'inline' | 'stack'
  /** Label column width in the `inline` layout. */
  labelWidth?: number
  /** Hairline separators between rows. */
  bordered?: boolean
  /** i18n resolver for `labelKey`. */
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  minColumnWidth: 240,
  maxColumns: 0,
  layout: 'inline',
  labelWidth: 120,
  bordered: false,
  translate: undefined,
})

defineSlots<{
  /** Override the label cell. */
  label?: (props: { item: DescriptionItem }) => unknown
  /** Override the value cell. */
  value?: (props: { item: DescriptionItem }) => unknown
}>()

const visibleItems = computed(() => props.items.filter((item) => !item.hidden))

const gridStyle = computed<CSSProperties>(() => {
  // `auto-fit` + `minmax` lets the CONTAINER pick the column count: it fits as
  // many tracks of at least `minColumnWidth` as will go, then stretches them.
  //
  // The cap is expressed by raising that per-track minimum to at least
  // `100% / maxColumns`, so auto-fit can never place more than N tracks while
  // the block still fills its container and still collapses to fewer columns on
  // a narrow one. Capping with `max-width` instead is what used to pin every
  // single-column read-only record to 280px with the rest of the row blank.
  const floor =
    props.maxColumns > 0
      ? `max(${props.minColumnWidth}px, ${100 / props.maxColumns}%)`
      : `${props.minColumnWidth}px`

  return { gridTemplateColumns: `repeat(auto-fit, minmax(min(100%, ${floor}), 1fr))` }
})

const labelStyle = computed<CSSProperties>(() =>
  props.layout === 'inline' ? { flexBasis: `${props.labelWidth}px`, width: `${props.labelWidth}px` } : {},
)

function rowStyle(item: DescriptionItem): CSSProperties {
  // A numeric span cannot be honoured precisely against a container-derived
  // column count (span 2 in a 1-column fold overflows the grid), so anything
  // wider than one column claims the full row instead.
  const wide = item.span === 'full' || (typeof item.span === 'number' && item.span >= 2)
  return wide ? { gridColumn: '1 / -1' } : {}
}

function resolveLabel(item: DescriptionItem): string {
  if (item.labelKey && props.translate) {
    const out = props.translate(item.labelKey)
    if (out) return out
  }
  return item.label
}

function isBlank(item: DescriptionItem): boolean {
  return !item.render && isEmptyValue(item.value)
}

/**
 * Value cell renderer. A custom `render()` always wins; otherwise a blank
 * value degrades to the shared dash and everything else is stringified.
 * Multi-line strings keep their line breaks (`white-space: pre-line` on the
 * value cell) so an address or a note is not flattened into one line.
 */
const TDescriptionValue: FunctionalComponent<{ item: DescriptionItem }> = (cellProps) => {
  const { item } = cellProps
  if (item.render) return item.render() as VNodeChild
  if (isEmptyValue(item.value)) return EMPTY_DASH
  if (Array.isArray(item.value)) return item.value.map((v) => String(v)).join(', ')
  if (typeof item.value === 'boolean') return boolLabel(item.value)
  return h('span', String(item.value))
}

/** Boolean values read as words, not as a checkbox glyph - translated when the
 *  host supplied a resolver, English otherwise. */
function boolLabel(value: boolean): string {
  const key = value ? 'admin.common.yes' : 'admin.common.no'
  const fallback = value ? 'Yes' : 'No'
  if (!props.translate) return fallback
  return props.translate(key) || fallback
}
</script>

<style scoped>
.t-desc {
  display: grid;
  column-gap: 24px;
  row-gap: 0;
  margin: 0;
}
.t-desc__row {
  display: flex;
  align-items: baseline;
  gap: 12px;
  min-width: 0;
  padding: 7px 0;
}
.t-desc--bordered .t-desc__row {
  border-bottom: 1px solid var(--tnzi-border);
}
.t-desc__label {
  flex-shrink: 0;
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted, #888);
}
.t-desc__value {
  flex: 1 1 auto;
  min-width: 0;
  margin: 0;
  font-size: 13.5px;
  line-height: 1.5;
  color: var(--tnzi-base-text);
  /* Keep author-intended line breaks (addresses, notes) without turning a
     one-line value into a <pre> block. */
  white-space: pre-line;
  overflow-wrap: anywhere;
}
.t-desc__value--empty {
  color: var(--tnzi-base-text-muted, #999);
}

/* Stack layout: label above value, full-width value. */
.t-desc--stack .t-desc__row {
  flex-direction: column;
  align-items: stretch;
  gap: 2px;
}
.t-desc--stack .t-desc__label {
  width: auto !important;
  flex-basis: auto !important;
  font-size: 12px;
}

/* Phones: the inline layout's fixed label column eats the value width, so it
   folds to the stacked shape regardless of the requested layout. */
@media (max-width: 640px) {
  .t-desc--inline .t-desc__row {
    flex-direction: column;
    align-items: stretch;
    gap: 2px;
  }
  .t-desc--inline .t-desc__label {
    width: auto !important;
    flex-basis: auto !important;
    font-size: 12px;
  }
}
</style>
