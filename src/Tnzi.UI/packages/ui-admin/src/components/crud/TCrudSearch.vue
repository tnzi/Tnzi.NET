<template>
  <NCard
    :bordered="false"
    size="small"
    class="t-crud-search t-crud-page__search-card t-crud-page__search"
  >
    <!-- ── Simple row - ALWAYS visible ──
         Keyword input + Search + (toggle) "Advanced ▾". Clicking the
         toggle expands the advanced grid below; clicking again collapses
         it (chevron flips ▾↔▴). -->
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

    <!-- ── Advanced panel - collapsible ── -->
    <NCollapseTransition v-if="hasAdvancedSearch" :show="advancedMode">
      <div class="t-crud-page__search-advanced-wrap">
        <TCrudSearchAdvanced
          :state="props.state"
          :search-fields="searchFields"
          :translate="translate"
        />
      </div>
    </NCollapseTransition>
  </NCard>
</template>

<script setup lang="ts">
import { computed, ref, type Ref } from 'vue'
import { NButton, NCard, NCollapseTransition, NInput } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudSearchAdvanced from './TCrudSearchAdvanced.vue'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'

/**
 * Minimum slice of `UseCrudPageReturn` that the search panel needs.
 * Decouples search from `T` / `TId` generics - the panel only writes back
 * `searchText` (string) and `filters` (Record<string, unknown>).
 */
export interface SearchableState {
  query: Ref<{ searchText: string }>
  setSearch: (text: string) => void
  setFilters: (filters: Record<string, unknown>) => void
  refresh: () => Promise<void>
}

/**
 * Search panel for {@link TCrudPage}: an always-visible simple keyword row
 * plus a collapsible advanced grid ({@link TCrudSearchAdvanced}). Wired to a
 * `useCrudPage` state - commit-side calls `state.setSearch(...)` /
 * `state.setFilters(...)` then `state.refresh()`.
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

// ── Simple search ──────────────────────────────────────────────────
const simpleQuery = ref<string>(props.state.query.value.searchText ?? '')
function onApplySimpleSearch(): void {
  props.state.setSearch(simpleQuery.value)
  void props.state.refresh()
}
function onClearSimpleSearch(): void {
  simpleQuery.value = ''
  props.state.setSearch('')
  void props.state.refresh()
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
</script>

<style scoped>
/* Card chrome - bordered=false NCard still carries a soft shadow +
   8px border-radius to match soybean. */
.t-crud-page__search-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  flex-shrink: 0;
}

/* Simple-mode row - horizontally centered cluster:
     [🔍 keyword input (clearable)]  [Search]  [Advanced (link)] */
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

/* Advanced panel - sits below the simple row, NCollapseTransition handles
   the height animation. Top margin separates it from the simple row. */
.t-crud-page__search-advanced-wrap {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px dashed var(--tnzi-border, #e5e7eb);
}
</style>
