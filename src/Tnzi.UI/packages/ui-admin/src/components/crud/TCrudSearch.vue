<template>
  <NCard
    :bordered="false"
    size="small"
    class="t-crud-search t-crud-page__search-card t-crud-page__search"
  >
    <!-- ── Simple row — ALWAYS visible ──
         Keyword input + Search + (toggle) "Advanced ▾". Clicking the
         toggle expands an advanced form panel below this row;
         clicking again collapses it (chevron flips ▾↔▴). The simple
         row itself never disappears, so users always have keyword
         search at hand without losing their advanced filter state. -->
    <div v-if="!hideSimpleMode" class="t-crud-page__search-simple">
      <NInput
        :value="simpleQuery"
        :placeholder="effectiveSearchPlaceholder"
        clearable
        class="t-crud-page__search-simple-input"
        @update:value="(v: string) => (simpleQuery = v)"
        @keydown.enter="onApplySimpleSearch"
        @clear="onClearSimpleSearch"
      >
        <template #prefix>
          <TSvgIcon icon="mdi:magnify" :size="16" />
        </template>
      </NInput>
      <NButton type="primary" @click="onApplySimpleSearch">
        {{ t('admin.crud.search') }}
      </NButton>
      <NButton
        v-if="hasAdvancedSearch"
        text
        class="t-crud-page__search-toggle"
        @click="advancedMode = !advancedMode"
      >
        {{ t('admin.crud.advancedSearch') }}
        <template #icon>
          <TSvgIcon
            :icon="advancedMode ? 'mdi:chevron-up' : 'mdi:chevron-down'"
            :size="16"
          />
        </template>
      </NButton>
    </div>

    <!-- ── Advanced panel — collapsible ──
         Hidden by default, expands below the simple row when user
         clicks the "Advanced ▾" toggle. Compact grid (no labels,
         placeholder carries the field name) keeps it visually
         secondary to the always-on keyword search. -->
    <NCollapseTransition v-if="hasAdvancedSearch" :show="advancedMode">
      <NForm
        class="t-crud-page__search-advanced"
        label-placement="left"
        :show-feedback="false"
        :show-require-mark="false"
      >
        <NGrid responsive="screen" item-responsive :x-gap="12" :y-gap="12">
          <!-- Span ladder: 1 col (xs) → 2 col (sm) → 3 col (md) →
               4 col (l+). Previously jumped 2→4 at the m breakpoint
               which left ~768-1023px viewports with awkwardly wide
               single-field rows. -->
          <NGi
            v-for="field in searchFields"
            :key="field.key"
            span="24 s:12 m:8 l:6"
          >
            <NFormItem :show-label="false" :path="field.key">
              <component :is="renderSearchField(field)" />
            </NFormItem>
          </NGi>
          <NGi
            :span="searchButtonSpan"
            class="t-crud-page__search-actions-cell"
          >
            <NSpace justify="end" class="t-crud-page__search-actions">
              <NButton type="primary" @click="onApplyAdvancedSearch">
                {{ t('admin.crud.search') }}
              </NButton>
            </NSpace>
          </NGi>
        </NGrid>
      </NForm>
    </NCollapseTransition>
  </NCard>
</template>

<script setup lang="ts">
import { computed, h, reactive, ref, watch, type Ref } from 'vue'
import {
  NButton,
  NCard,
  NCollapseTransition,
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
import { TSvgIcon } from '@tnzi/ui'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'

/**
 * Minimum slice of `UseCrudPageReturn` that TCrudSearch needs. Decouples
 * the search panel from `T` / `TId` generics — those don't matter here
 * because the panel only writes back `searchText` (string) and `filters`
 * (Record<string, unknown>). Using this interface instead of the full
 * `UseCrudPageReturn<T, TId>` keeps the parent → child contract narrow
 * and avoids spurious generic-variance compile errors.
 */
export interface SearchableState {
  query: Ref<{ searchText: string }>
  setSearch: (text: string) => void
  setFilters: (filters: Record<string, unknown>) => void
  refresh: () => Promise<void>
}

/**
 * Search panel for {@link TCrudPage}. Sunk out of the parent monolith in
 * 0.2.72+ (B5) so the simple/advanced search surface can be reused and
 * unit-tested in isolation. Wired directly to a `useCrudPage` state —
 * commit-side calls `state.setSearch(...)` / `state.setFilters(...)`
 * followed by `state.refresh()`, mirroring the previous in-line logic.
 */
interface Props {
  /** State produced by `useCrudPage<T>`. Used to commit search/filters. */
  state: SearchableState
  /** Advanced-search field schema (each field renders a typed input). */
  searchFields?: FormSchemaItem[]
  /** Placeholder for the simple keyword box. */
  searchPlaceholder?: string
  /** When `true`, opens in advanced mode initially. */
  defaultAdvancedMode?: boolean
  /** When `true`, the simple keyword row is never rendered (advanced only). */
  hideSimpleMode?: boolean
  /** Translation helper. */
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  searchFields: undefined,
  searchPlaceholder: undefined,
  defaultAdvancedMode: false,
  hideSimpleMode: false,
  translate: undefined,
})

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function maybeTranslate(value: string | undefined, fallback: string): string {
  if (!value) return fallback
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) {
    return t(value)
  }
  return value
}

// ── Simple search ──────────────────────────────────────────────────
const simpleQuery = ref<string>(props.state.query.value.searchText ?? '')
function onApplySimpleSearch(): void {
  props.state.setSearch(simpleQuery.value)
  void props.state.refresh()
}
function onResetSimpleSearch(): void {
  simpleQuery.value = ''
  props.state.setSearch('')
  void props.state.refresh()
}
function onClearSimpleSearch(): void {
  onResetSimpleSearch()
}

// ── Mode toggle ────────────────────────────────────────────────────
const advancedMode = ref(
  props.hideSimpleMode || (props.defaultAdvancedMode && !!props.searchFields?.length),
)

const hasAdvancedSearch = computed(
  () => !props.hideSimpleMode && !!props.searchFields?.length,
)

const effectiveSearchPlaceholder = computed(
  () => props.searchPlaceholder ?? t('admin.crud.searchPlaceholder'),
)

// ── Advanced search ────────────────────────────────────────────────
const searchModel = reactive<Record<string, unknown>>({})

const searchButtonSpan = computed<string>(() => {
  const count = props.searchFields?.length ?? 0
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
    for (const f of fields) {
      if (!(f.key in searchModel)) searchModel[f.key] = null
    }
  },
  { immediate: true, deep: false },
)

function renderSearchField(item: FormSchemaItem): unknown {
  const value = searchModel[item.key]
  const onUpdate = (v: unknown) => {
    searchModel[item.key] = v
  }
  const effectiveType = item.typeFn ? item.typeFn(searchModel) : item.type
  const placeholder = maybeTranslate(item.placeholder ?? item.label, item.label)
  switch (effectiveType) {
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
        options: item.options ?? [],
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

function onApplyAdvancedSearch(): void {
  const filters: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(searchModel)) {
    if (v !== null && v !== undefined && v !== '') filters[k] = v
  }
  props.state.setFilters(filters)
  void props.state.refresh()
}
</script>

<style scoped>
/* 0.2.72+ (B5): search-related layout styles moved here from
   TCrudPage.vue when the markup was extracted. TCrudPage's
   `<style scoped>` doesn't reach into child SFCs, so the simple-row
   flex layout was breaking (Search + Advanced buttons wrapping to a
   second line) until the rules followed the markup. */

/* Card chrome — bordered=false NCard still carries a soft shadow +
   8px border-radius to match soybean. */
.t-crud-page__search-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  flex-shrink: 0;
}

/* Simple-mode row — horizontally centered cluster:
     [🔍 keyword input (clearable)]  [Search]  [Advanced (link)]
   Reset is intentionally omitted — NInput's built-in × clears and
   refreshes (see `@clear="onClearSimpleSearch"`). */
.t-crud-page__search-simple {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  flex-wrap: wrap;
}
.t-crud-page__search-simple-input {
  flex: 0 1 420px;
  min-width: 240px;
}

/* Mobile: search input gives up its 240px minimum and stretches to
   fill the card so the Search + Advanced buttons drop to a second
   row without the input clipping the card edge. */
@media (max-width: 640px) {
  .t-crud-page__search-simple {
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
  }
  .t-crud-page__search-simple-input {
    flex: 1 1 100%;
    min-width: 0;
    width: 100%;
  }
}

.t-crud-page__search-toggle :deep(.n-button__content) {
  color: var(--tnzi-base-text-muted, #6b7280);
}
.t-crud-page__search-toggle:hover :deep(.n-button__content) {
  color: var(--tnzi-primary, #06b6d4);
}

.t-crud-page__search-actions-cell {
  /* Modest right indent so Search button doesn't kiss the card edge. */
  padding-right: 12px;
}
.t-crud-page__search-actions {
  width: 100%;
}

/* Advanced form panel — sits below the simple row, NCollapseTransition
   handles the height animation. Top margin separates it visually from
   the always-visible simple row above. */
.t-crud-page__search-advanced {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px dashed var(--tnzi-border, #e5e7eb);
}
</style>
