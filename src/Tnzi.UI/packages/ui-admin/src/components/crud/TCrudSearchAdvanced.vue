<template>
  <NForm
    class="t-crud-page__search-advanced t-crud-search-advanced"
    :label-placement="labeled ? 'top' : 'left'"
    :show-feedback="false"
    :show-require-mark="false"
  >
    <NGrid responsive="screen" item-responsive :x-gap="12" :y-gap="12">
      <!-- Compact (default): label-less, placeholder carries the field name,
           responsive span ladder 1/2/3/4. Labeled (drawer): one field per row,
           full label on top. -->
      <NGi v-for="field in shownFields" :key="field.key" :span="labeled ? '24' : '24 s:12 m:8 l:6'">
        <NFormItem :show-label="labeled" :label="labeled ? fieldLabel(field) : undefined" :path="field.key">
          <component :is="renderSearchField(field)" />
        </NFormItem>
      </NGi>
      <NGi v-if="!hideSubmit" :span="searchButtonSpan" class="t-crud-page__search-actions-cell">
        <NSpace justify="end" class="t-crud-page__search-actions">
          <NButton type="primary" @click="onApplyAdvancedSearch">
            {{ t('admin.crud.search') }}
          </NButton>
        </NSpace>
      </NGi>
    </NGrid>
  </NForm>
</template>

<script setup lang="ts">
import { computed, h, reactive, watch, type VNodeChild } from 'vue'
import {
  NButton,
  NDatePicker,
  NForm,
  NFormItem,
  NGi,
  NGrid,
  NInput,
  NInputNumber,
  NSelect,
  NSpace,
  NSwitch,
} from 'naive-ui'
import type { FormSchemaItem } from '@tnzi/ui'
import type { SearchableState } from './TCrudSearch.vue'
import { isoDateToTs, tsToIsoDate } from '../../utils/calendar-date'

/**
 * A search field, optionally carrying a custom `render` escape hatch. When
 * `render` is set it takes precedence over the built-in `type` switch, so a
 * page can drop a daterange / cascader / any bespoke control into advanced
 * search without the renderer needing to know the type. `render` receives the
 * live search model and should write back via the model (e.g.
 * `model.foo = v`). Backward compatible: every existing `FormSchemaItem[]`
 * still satisfies this (render is optional).
 */
export interface SearchFieldItem extends FormSchemaItem {
  render?: (model: Record<string, unknown>) => VNodeChild
}

/**
 * Advanced (multi-field) search grid. Extracted from {@link TCrudSearch} so
 * the same grid can be reused in two layouts:
 *  - inside {@link TCrudSearch} (collapsed below the simple keyword row), and
 *  - inside {@link TListShell}'s white page-header card (the search lives in
 *    the header bar; the advanced grid drops below it on toggle).
 * Owns its own `searchModel`, seeded from the filters in effect
 * (`state.query.filters`); commits via `state.setFilters(...)` + refresh.
 */
interface Props {
  state: SearchableState
  searchFields?: SearchFieldItem[]
  translate?: (key: string) => string
  /** Hide the inline Search button (when an external footer drives apply/reset). */
  hideSubmit?: boolean
  /** Labeled layout: one field per row with its label on top (drawer use).
      Default (false) is the compact, label-less placeholder grid. */
  labeled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  searchFields: undefined,
  translate: undefined,
  hideSubmit: false,
  labeled: false,
})

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function maybeTranslate(value: string | undefined, fallback: string): string {
  if (!value) return fallback
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) return t(value)
  return value
}

/**
 * A schema key through the translator, the literal when there is no key or no
 * translator. Same rule as the create / edit forms (`TSchemaForm`), so a field
 * reads the same in its form and in its search.
 */
function tr(key: string | undefined, fallback: string): string {
  if (!key || !props.translate) return fallback
  return props.translate(key) || fallback
}

function fieldLabel(item: FormSchemaItem): string {
  return item.labelKey ? tr(item.labelKey, item.label) : maybeTranslate(item.label, item.label)
}

function fieldPlaceholder(item: FormSchemaItem): string {
  if (item.placeholderKey) return tr(item.placeholderKey, item.placeholder ?? fieldLabel(item))
  return item.placeholder ? maybeTranslate(item.placeholder, item.placeholder) : fieldLabel(item)
}

function fieldOptions(item: FormSchemaItem): Array<{ label: string; value: string | number }> {
  return (item.options ?? []).map((o) => ({ label: tr(o.labelKey, o.label), value: o.value }))
}

function isShown(item: SearchFieldItem): boolean {
  return !item.visible || item.visible(searchModel)
}

/** The fields on screen: a `visible()` that answers false hides its field. */
const shownFields = computed(() => (props.searchFields ?? []).filter(isShown))

const searchModel = reactive<Record<string, unknown>>({})

/** The filters in effect on the list, which this form shows and edits. */
function liveFilters(): Readonly<Record<string, unknown>> {
  return props.state.query.value.filters ?? {}
}

function effectiveTypeOf(item: SearchFieldItem): string | undefined {
  return item.typeFn ? item.typeFn(searchModel) : item.type
}

/**
 * Fill the given fields from the filters in effect.
 *
 * The form is not the only holder of what is applied: the drawer and the phone
 * panel unmount whenever they close, and a page can set a filter itself (a
 * deep-linked id). A form that started empty showed nothing for a filter that
 * was still narrowing the list, and its next Search replaced the filters with
 * only what was on screen, silently dropping the rest. A date field shows the
 * calendar date it sent back as the picker's timestamp.
 */
function seedFromFilters(fields: readonly SearchFieldItem[]): void {
  const filters = liveFilters()
  for (const f of fields) {
    const v = filters[f.key]
    searchModel[f.key] = v === undefined || v === '' ? null : v
  }
  for (const f of fields) {
    if (!f.render && effectiveTypeOf(f) === 'date' && searchModel[f.key] !== null) {
      searchModel[f.key] = isoDateToTs(searchModel[f.key])
    }
  }
}

const searchButtonSpan = computed<string>(() => {
  const count = shownFields.value.length
  if (count === 0) return '24'
  const sUsed = count % 2
  const mUsed = count % 3
  const lUsed = count % 4
  const sSpan = sUsed === 0 ? 12 : (2 - sUsed) * 12
  const mSpan = mUsed === 0 ? 12 : (3 - mUsed) * 8
  const lSpan = lUsed === 0 ? 12 : (4 - lUsed) * 6
  return `24 s:${sSpan} m:${mSpan} l:${lSpan}`
})

watch(
  () => props.searchFields,
  (fields) => {
    if (!fields) return
    for (const key of Object.keys(searchModel)) {
      if (!fields.find((f) => f.key === key)) delete searchModel[key]
    }
    // Only fields new to the form: one already on screen may hold an edit
    // that has not been applied yet.
    seedFromFilters(fields.filter((f) => !(f.key in searchModel)))
  },
  { immediate: true, deep: false },
)

// The filters change under the form when it applies them (no visible change)
// and when something else does: a deep link, or the page itself.
watch(
  () => props.state.query.value.filters,
  () => seedFromFilters(props.searchFields ?? []),
)

function renderSearchField(item: SearchFieldItem): unknown {
  // Custom render escape hatch wins over the builtin type switch - lets a page
  // drop a daterange / cascader / bespoke control into advanced search.
  if (item.render) return item.render(searchModel)
  const value = searchModel[item.key]
  const onUpdate = (v: unknown) => {
    searchModel[item.key] = v
  }
  const placeholder = fieldPlaceholder(item)
  switch (effectiveTypeOf(item)) {
    case 'text':
      return h(NInput, {
        value: (value as string | null) ?? null,
        placeholder,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'textarea':
      return h(NInput, {
        value: (value as string | null) ?? null,
        type: 'textarea',
        placeholder,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'number':
      return h(NInputNumber, {
        value: (value as number | null) ?? null,
        min: item.min,
        max: item.max,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'switch':
      return h(NSwitch, { value: value as boolean, 'onUpdate:value': onUpdate })
    case 'select':
      return h(NSelect, {
        value: (value as string | number | null) ?? null,
        options: fieldOptions(item),
        clearable: true,
        placeholder,
        'onUpdate:value': onUpdate,
      })
    case 'date':
      return h(NDatePicker, {
        value: value as number | null,
        clearable: true,
        type: 'date',
        'onUpdate:value': onUpdate,
      })
    default:
      return null
  }
}

/**
 * The value a field contributes to the filters. A `date` field holds the
 * picker's local-midnight timestamp, but list endpoints bind their date filters
 * to `DateTime?` and a number on the query string fails that binding (400, and
 * the list never loads). What was picked is a calendar date, so that is what
 * the filter carries: `YYYY-MM-DD`. An endpoint that compares timestamps turns
 * it into the bounds of that day in its bridge (`toLocalDayBounds`).
 */
function filterValue(item: SearchFieldItem | undefined, value: unknown): unknown {
  if (!item || item.render) return value
  return effectiveTypeOf(item) === 'date' && typeof value === 'number' ? tsToIsoDate(value) : value
}

/**
 * The filters in effect that no field of this form shows (a page's own tab or
 * category filter, set with `setFilters`). Applying or resetting the form
 * writes its own fields only; these were never on screen to be cleared.
 */
function filtersNotShown(): Record<string, unknown> {
  const kept: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(liveFilters())) {
    if (!(k in searchModel)) kept[k] = v
  }
  return kept
}

function onApplyAdvancedSearch(): void {
  const filters = filtersNotShown()
  for (const [k, v] of Object.entries(searchModel)) {
    if (v === null || v === undefined || v === '') continue
    const field = props.searchFields?.find((f) => f.key === k)
    // A hidden field narrows nothing: there is no box on screen to show or clear its value.
    if (field && !isShown(field)) continue
    filters[k] = filterValue(field, v)
  }
  props.state.setFilters(filters)
  void props.state.refresh()
}

function resetAdvancedSearch(): void {
  const kept = filtersNotShown()
  for (const key of Object.keys(searchModel)) searchModel[key] = null
  props.state.setFilters(kept)
  void props.state.refresh()
}

defineExpose({ apply: onApplyAdvancedSearch, reset: resetAdvancedSearch })
</script>

<style scoped>
/* No card chrome - the advanced grid is always embedded inside a parent
   container (TCrudSearch's collapse panel or TListShell's header card). */
.t-crud-page__search-actions-cell {
  padding-right: 12px;
}
.t-crud-page__search-actions {
  width: 100%;
}
</style>
