<template>
  <div class="t-list-shell" :class="`t-list-shell--${mode}`">
    <!-- ── WHITE page-header card ──
         Left: icon + title + ⓘ help. Right (#actions): the keyword search
         (small) + Advanced toggle. The advanced grid drops below the bar,
         still inside this white card. Consumers can replace the whole
         header via the #header slot, or drop it entirely with
         `:show-header="false"` (e.g. when the shell is embedded inside a
         page that already owns the single title bar). -->
    <slot v-if="props.showHeader && $slots.header" name="header" />
    <NCard
      v-else-if="props.showHeader"
      :bordered="false"
      size="small"
      class="t-list-shell__header-card"
    >
      <TPageHeader
        :title="props.title"
        :icon="props.icon"
        :help="props.titleHelp"
        :help-title="props.titleHelpTitle"
        :inline-actions="bp.isSm.value"
        :translate="props.translate"
      >
        <template v-if="showSearchUi && !hideSimpleMode" #actions>
          <!-- Desktop / tablet: inline keyword input + Advanced opens a right
               drawer. -->
          <div v-if="!bp.isSm.value" class="t-list-shell__search">
            <!-- showDefaultSearch=false 只隐藏关键词框(后端无自由文本查询的页面),
                 searchFields 驱动的 Advanced 仍保留 -->
            <template v-if="showDefaultSearch">
              <NInput
                v-model:value="simpleQuery"
                size="small"
                clearable
                :placeholder="searchPlaceholder ?? t('admin.crud.searchPlaceholder')"
                class="t-list-shell__search-input"
                @keydown.enter="onSimpleSearch"
                @clear="onSimpleClear"
              >
                <template #prefix><TSvgIcon icon="mdi:magnify" :size="16" /></template>
              </NInput>
              <NButton size="small" type="primary" @click="onSimpleSearch">
                {{ t('admin.crud.search') }}
              </NButton>
            </template>
            <NButton
              v-if="hasAdvanced"
              size="small"
              tertiary
              class="t-list-shell__adv-toggle"
              @click="advancedOpen = true"
            >
              <template #icon><TSvgIcon icon="mdi:filter-variant" :size="16" /></template>
              {{ t('admin.crud.advancedSearch') }}
            </NButton>
          </div>

          <!-- Phone: title stays left; the right shows a 🔍 toggle (+ an
               Advanced toggle when there are advanced fields). Tapping either
               expands its form downward inside this white card. -->
          <div v-else class="t-list-shell__search t-list-shell__search--mobile">
            <NButton
              v-if="showDefaultSearch"
              size="small"
              :type="mobilePanel === 'keyword' ? 'primary' : 'default'"
              :tertiary="mobilePanel !== 'keyword'"
              circle
              class="t-list-shell__search-icon"
              :aria-label="t('admin.crud.search')"
              @click="toggleMobilePanel('keyword')"
            >
              <template #icon><TSvgIcon icon="mdi:magnify" :size="18" /></template>
            </NButton>
            <NButton
              v-if="hasAdvanced"
              size="small"
              :type="mobilePanel === 'advanced' ? 'primary' : 'default'"
              :tertiary="mobilePanel !== 'advanced'"
              class="t-list-shell__adv-toggle"
              @click="toggleMobilePanel('advanced')"
            >
              <template #icon><TSvgIcon icon="mdi:filter-variant" :size="16" /></template>
              {{ t('admin.crud.advancedSearch') }}
            </NButton>
          </div>
        </template>
      </TPageHeader>

      <!-- Phone downward-expanding search panel — one field per row, the
           action buttons pinned to the right of their own row. -->
      <div
        v-if="bp.isSm.value && mobilePanel !== 'none'"
        class="t-list-shell__mobile-search"
      >
        <template v-if="mobilePanel === 'keyword'">
          <NInput
            v-model:value="simpleQuery"
            clearable
            :placeholder="searchPlaceholder ?? t('admin.crud.searchPlaceholder')"
            class="t-list-shell__mobile-field"
            @keydown.enter="onSimpleSearch"
            @clear="onSimpleClear"
          >
            <template #prefix><TSvgIcon icon="mdi:magnify" :size="16" /></template>
          </NInput>
          <div class="t-list-shell__mobile-actions">
            <NButton type="primary" class="t-list-shell__mobile-submit" @click="onSimpleSearch">
              {{ t('admin.crud.search') }}
            </NButton>
          </div>
        </template>
        <template v-else>
          <TCrudSearchAdvanced
            ref="mobileAdvRef"
            :state="props.state"
            :search-fields="searchFields"
            :translate="translate"
            hide-submit
            labeled
          />
          <div class="t-list-shell__mobile-actions">
            <NButton class="t-list-shell__mobile-reset" @click="onMobileAdvReset">
              {{ t('admin.crud.reset') }}
            </NButton>
            <NButton type="primary" class="t-list-shell__mobile-submit" @click="onMobileAdvSearch">
              {{ t('admin.crud.search') }}
            </NButton>
          </div>
        </template>
      </div>

      <!-- Desktop advanced drawer (phones use the inline panel above). -->
      <TCrudSearchDrawer
        v-if="hasAdvanced && !bp.isSm.value"
        v-model:show="advancedOpen"
        :state="props.state"
        :search-fields="searchFields"
        :translate="translate"
      />
    </NCard>

    <!-- Consumer custom search area (full replacement). -->
    <NCard
      v-if="$slots.search"
      :bordered="false"
      size="small"
      class="t-list-shell__search-card"
    >
      <slot name="search" />
    </NCard>

    <!-- Optional KPI strip — sits between the white header card and the list
         card (content-page standard: header → KPI row → list). Typically a
         `TKpiRow` of `TKpiCard`s. -->
    <div v-if="$slots.kpis" class="t-list-shell__kpis">
      <slot name="kpis" />
    </div>

    <slot
      v-if="props.state.error.value"
      name="error"
      :error="props.state.error.value"
      :retry="() => props.state.refresh()"
      :dismiss="() => props.state.dismissError()"
    >
      <NAlert
        type="error"
        class="t-list-shell__error"
        :title="t('admin.crud.fetchError')"
        closable
        @close="props.state.dismissError"
      >
        <div class="t-list-shell__error-body">
          <span class="t-list-shell__error-msg">{{ props.state.error.value.message }}</span>
          <NButton size="small" tertiary @click="() => props.state.refresh()">
            {{ t('admin.crud.retry') }}
          </NButton>
        </div>
      </NAlert>
    </slot>

    <!-- ── LIST card ──
         Toolbar (#header-extra): create / refresh / export / import /
         column-settings (#toolbar) — these stay in the list container.
         Optional left filters via #toolbarLeft. -->
    <NCard
      :bordered="false"
      size="small"
      class="t-list-shell__list-card"
    >
      <!-- Toolbar row inside the list container: left filters + right
           action buttons (create / refresh / export / import / columns).
           Rendered in the card content (not the NCard header) so it shows
           reliably even without a card title. Only rendered when it has
           something to show — a list with no actions/filters skips it
           entirely (no empty gap above the table). -->
      <div v-if="hasToolbar" class="t-list-shell__toolbar">
        <div class="t-list-shell__toolbar-left">
          <slot name="toolbarLeft" />
        </div>
        <div class="t-list-shell__actions">
          <slot name="primary">
            <NButton
              v-if="showCreate && props.state.canCreate !== false"
              type="primary"
              tertiary
              size="small"
              class="t-list-shell__action t-list-shell__create"
              @click="props.state.openCreate"
            >
              <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
              {{ t('admin.crud.create') }}
            </NButton>
          </slot>
          <NButton v-if="showExport" tertiary size="small" class="t-list-shell__action" @click="onExport">
            <template #icon><TSvgIcon icon="mdi:download-outline" :size="16" /></template>
            {{ t('admin.crud.export') }}
          </NButton>
          <NButton v-if="showImport" tertiary size="small" class="t-list-shell__action" @click="onImport">
            <template #icon><TSvgIcon icon="mdi:upload-outline" :size="16" /></template>
            {{ t('admin.crud.import') }}
          </NButton>
          <slot name="toolbarRight" />
          <NTooltip v-if="showRefresh" placement="top" :delay="600">
            <template #trigger>
              <NButton
                tertiary
                size="small"
                class="t-list-shell__action t-list-shell__refresh t-crud-toolbar__refresh t-list-shell__trailing-icon"
                :aria-label="t('admin.crud.refresh')"
                @click="onRefresh"
              >
                <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
              </NButton>
            </template>
            {{ t('admin.crud.refresh') }}
          </NTooltip>
          <slot name="toolbar" />
        </div>
      </div>

      <div class="t-list-shell__body">
        <slot name="renderer" />
      </div>

      <div class="t-list-shell__footer">
        <div class="t-list-shell__footer-left">
          <template v-if="showBatch && !batchSelectionEmpty">
            <slot name="batchActions" :selectedIds="props.state.batchActions.selectedIds.value" />
            <NPopconfirm v-if="props.state.canDelete !== false" @positive-click="onBatchDelete">
              <template #trigger>
                <NButton type="error" size="small" ghost>
                  <template #icon><TSvgIcon icon="mdi:trash-can-outline" :size="14" /></template>
                  {{ t('admin.crud.batchDelete') }} ({{ props.state.batchActions.selectedCount.value }})
                </NButton>
              </template>
              {{ batchDeleteConfirmText }}
            </NPopconfirm>
            <NButton size="small" @click="props.state.batchActions.clear">
              <template #icon><TSvgIcon icon="mdi:close-circle-outline" :size="14" /></template>
              {{ t('admin.crud.unselectAll') }}
            </NButton>
          </template>
        </div>
        <NPagination
          v-if="showPagination"
          v-bind="paginationConfig"
          @update:page="props.state.setPage"
          @update:page-size="props.state.setPageSize"
        />
      </div>
    </NCard>

    <TFormModal
      v-if="props.state.canCreate !== false || props.state.canUpdate !== false || !!$slots.detail"
      :state="formModalState"
      :title="modalTitle"
      :translate="props.translate"
      :width="formModalWidth"
      :skip-view-mode="!!$slots.detail"
      @submit="props.state.submit"
    >
      <template #default="{ formData, mode: m }">
        <slot name="form" :formData="castFormData(formData)" :mode="m" />
      </template>
      <template #footer><slot name="formFooter" /></template>
    </TFormModal>

    <!-- Read-only VIEW drawer. Same `formModal` open-state as create/edit (so it
         is deep-linkable + Back-closeable for free), but a different chrome:
         `view` action → this right drawer with the page's `#detail` body;
         create/edit → the modal above. Rendered only when the page supplies a
         `#detail` slot — otherwise `view` stays in the form modal (back-compat). -->
    <TDrawerShell
      v-if="$slots.detail"
      :show="props.state.formModal.visible.value && props.state.formModal.mode.value === 'view'"
      :width="detailWidth"
      :title="detailTitleText"
      @update:show="(v: boolean) => { if (!v) props.state.formModal.close() }"
    >
      <slot
        name="detail"
        :data="castFormData(props.state.formModal.formData.value)"
        :mode="props.state.formModal.mode.value"
      />
      <template v-if="$slots.detailFooter" #footer>
        <slot name="detailFooter" :data="castFormData(props.state.formModal.formData.value)" />
      </template>
    </TDrawerShell>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed, ref, useSlots, watch } from 'vue'
import { NAlert, NButton, NCard, NInput, NPagination, NPopconfirm, NTooltip } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TFormModal from './TFormModal.vue'
import TDrawerShell from '../overlay/TDrawerShell.vue'
import TCrudSearchDrawer from './TCrudSearchDrawer.vue'
import TCrudSearchAdvanced from './TCrudSearchAdvanced.vue'
import TPageHeader from '../layout/TPageHeader.vue'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'
import type { UseFormModalReturn, FormModalMode } from '../../headless/useFormModal'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'
import { useBreakpoint } from '../../headless/useBreakpoint'

export interface TListShellProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  title?: string
  icon?: string
  /** `page` (default) flex-fills the page height; `container` is content-sized for embedding. */
  mode?: 'page' | 'container'
  /** When false the whole white header card (TPageHeader + search) is not
      rendered — for shells nested inside a page that already owns the single
      title bar (otherwise TPageHeader falls back to the route meta title and
      renders a duplicate bar). Default true. */
  showHeader?: boolean
  showSearch?: boolean
  showDefaultSearch?: boolean
  searchFields?: FormSchemaItem[]
  searchPlaceholder?: string
  /** When true (and searchFields exist), opens the advanced-search drawer immediately on mount. */
  defaultAdvancedMode?: boolean
  hideSimpleMode?: boolean
  showCreate?: boolean
  showExport?: boolean
  showImport?: boolean
  showRefresh?: boolean
  showBatch?: boolean
  showPagination?: boolean
  formModalWidth?: number
  /** Width of the read-only view drawer (the `#detail` slot). Default 640.
      Accepts a string (e.g. `'100vw'`) for responsive full-screen on phones. */
  detailWidth?: number | string
  /** Title for the view drawer, derived from the viewed record. */
  detailTitle?: (data: T) => string
  titleHelp?: string
  titleHelpTitle?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TListShellProps<T, TId>>(), {
  title: undefined,
  icon: undefined,
  mode: 'page',
  showHeader: true,
  showSearch: true,
  showDefaultSearch: true,
  searchFields: undefined,
  searchPlaceholder: undefined,
  defaultAdvancedMode: false,
  hideSimpleMode: false,
  showCreate: true,
  showExport: false,
  showImport: false,
  showRefresh: true,
  showBatch: true,
  showPagination: true,
  formModalWidth: 560,
  detailWidth: 640,
  detailTitle: undefined,
  titleHelp: undefined,
  titleHelpTitle: undefined,
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  kpis?: () => unknown
  search?: () => unknown
  primary?: () => unknown
  toolbar?: () => unknown
  toolbarLeft?: () => unknown
  toolbarRight?: () => unknown
  batchActions?: (props: { selectedIds: TId[] }) => unknown
  renderer?: () => unknown
  error?: (props: { error: Error; retry: () => Promise<void>; dismiss: () => void }) => unknown
  form?: (props: { formData: Partial<T> | null; mode: FormModalMode | null }) => unknown
  formFooter?: () => unknown
  detail?: (props: { data: Partial<T> | null; mode: FormModalMode | null }) => unknown
  detailFooter?: (props: { data: Partial<T> | null }) => unknown
}>()

const bp = useBreakpoint()
const slots = useSlots()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

// The list-card toolbar renders only when it has at least one action or
// filter — otherwise it's skipped so the table doesn't get an empty gap
// above it. The card content owns its own top inset (CSS), independent of
// whether this toolbar exists.
const hasToolbar = computed(
  () =>
    !!slots.toolbarLeft ||
    !!slots.primary ||
    !!slots.toolbar ||
    !!slots.toolbarRight ||
    (props.showCreate && props.state.canCreate !== false) ||
    props.showRefresh ||
    props.showExport ||
    props.showImport,
)

// ── Search (in the white header card) ──────────────────────────────
const showSearchUi = computed(() => props.showSearch && (!!props.searchFields || props.showDefaultSearch))
const hasAdvanced = computed(() => !!props.searchFields?.length)
const simpleQuery = ref<string>(props.state.query.value.searchText ?? '')
const advancedOpen = ref(props.defaultAdvancedMode && !!props.searchFields?.length)
// Phone search panel state: 'none' | 'keyword' | 'advanced'. When
// `defaultAdvancedMode` is set (and fields exist) the advanced panel opens
// immediately, mirroring the desktop drawer's auto-open.
const mobilePanel = ref<'none' | 'keyword' | 'advanced'>(
  props.defaultAdvancedMode && !!props.searchFields?.length ? 'advanced' : 'none',
)
const mobileAdvRef = ref<{ apply: () => void; reset: () => void } | null>(null)
function toggleMobilePanel(mode: 'keyword' | 'advanced'): void {
  mobilePanel.value = mobilePanel.value === mode ? 'none' : mode
}
function onMobileAdvSearch(): void {
  mobileAdvRef.value?.apply()
}
function onMobileAdvReset(): void {
  mobileAdvRef.value?.reset()
}
// Collapse the phone panel when widening back to desktop, so returning to a
// narrow width starts from the icon toggles again.
watch(
  () => bp.isSm.value,
  (narrow) => {
    if (!narrow) mobilePanel.value = 'none'
  },
)
function onSimpleSearch(): void {
  props.state.setSearch(simpleQuery.value)
  void props.state.refresh()
}
function onSimpleClear(): void {
  simpleQuery.value = ''
  props.state.setSearch('')
  void props.state.refresh()
}

const formModalState = computed(
  () => props.state.formModal as unknown as UseFormModalReturn<unknown>,
)
function castFormData(data: unknown): Partial<T> | null {
  return data as Partial<T> | null
}

// The view drawer renders only when the page supplies a `#detail` body — gated
// in the template via the reactive `$slots.detail` (a `computed(() => slots.x)`
// would NOT re-evaluate when a conditional slot appears, so use `$slots`
// directly). Its title is derived from the viewed record via `detailTitle`.
const detailTitleText = computed(() => {
  const d = props.state.formModal.formData.value
  return props.detailTitle && d ? props.detailTitle(d as T) : ''
})

function onRefresh(): void {
  void props.state.refresh()
}
async function onExport(): Promise<void> {
  const blob = await props.state.exportAll()
  if (blob && typeof URL !== 'undefined' && typeof document !== 'undefined') {
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${props.title ?? 'export'}.csv`
    document.body.appendChild(anchor)
    anchor.click()
    document.body.removeChild(anchor)
    URL.revokeObjectURL(url)
  }
}
function onImport(): void {
  if (typeof document === 'undefined') return
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.csv,.xlsx,.json'
  input.onchange = (e: Event) => {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (file) void props.state.importFile(file)
  }
  input.click()
}

const batchSelectionEmpty = computed(() => props.state.batchActions.selectedCount.value === 0)
const batchDeleteConfirmText = computed(() => {
  const count = props.state.batchActions.selectedCount.value
  const tpl = t('admin.crud.confirmBatchDelete')
  if (tpl && tpl !== 'admin.crud.confirmBatchDelete') return tpl.replace('{n}', String(count))
  return t('admin.crud.confirmDelete')
})
function onBatchDelete(): void {
  const ids = props.state.batchActions.selectedIds.value
  if (ids.length === 0) return
  void props.state.handleDelete(ids as TId[])
}

const modalTitle = computed(() => {
  const mode = props.state.formModal.mode.value
  return mode ? t(`admin.crud.${mode}Title`) : ''
})

const paginationConfig = computed(() => {
  const compact = bp.isSm.value
  const base = {
    page: props.state.query.value.pageIndex,
    pageSize: props.state.query.value.pageSize,
    itemCount: props.state.total.value,
  }
  if (compact) return { ...base, simple: true, showSizePicker: false }
  return {
    ...base,
    showSizePicker: true,
    pageSizes: [10, 20, 50, 100],
    prefix: ({ itemCount }: { itemCount: number | undefined }) => `${t('admin.crud.total')} ${itemCount ?? 0}`,
  }
})
</script>

<style scoped>
/* Spacing convention: ONE 12px scale. Cards carry a single uniform 12px
   inset; vertical rhythm comes from flex-column `gap` (gap-y), NOT per-child
   padding/margin; cards are separated by the root `gap`. */
.t-list-shell {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
}
.t-list-shell--page {
  height: 100%;
  min-height: 0;
  overflow: hidden;
}
.t-list-shell--container {
  height: auto;
}

/* Card chrome shared by every card in the shell. */
.t-list-shell__header-card,
.t-list-shell__search-card,
.t-list-shell__list-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
.t-list-shell__header-card,
.t-list-shell__search-card { flex-shrink: 0; }

/* One uniform 12px inset for every card in the shell. */
.t-list-shell__header-card :deep(.n-card-content),
.t-list-shell__search-card :deep(.n-card-content),
.t-list-shell__list-card :deep(.n-card-content) {
  padding: 12px;
}
/* The list card's content is a flex column whose vertical rhythm is owned by
   `gap` (gap-y) — toolbar / body / footer add no padding or margin of their
   own. */
.t-list-shell__list-card :deep(.n-card-content) {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

/* List card fills the page; its content scrolls internally (table owns the
   scroll, footer stays pinned). */
.t-list-shell--page .t-list-shell__list-card {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}
.t-list-shell--page .t-list-shell__list-card :deep(.n-card-content) {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}
.t-list-shell--page .t-list-shell__body {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.t-list-shell__kpis { flex-shrink: 0; }

.t-list-shell__error { flex-shrink: 0; }
.t-list-shell__error-body { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.t-list-shell__error-msg { word-break: break-word; flex: 1 1 auto; }

/* Toolbar + footer carry no own padding/margin — card inset + flex gap space them. */
.t-list-shell__toolbar {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.t-list-shell__toolbar-left { display: flex; align-items: center; gap: 8px; min-width: 0; }
.t-list-shell__footer {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.t-list-shell__footer-left { display: flex; align-items: center; gap: 8px; min-height: 28px; min-width: 0; }
.t-list-shell__actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }
.t-list-shell__action { font-weight: 400; }
/* Trailing icon-only buttons (Refresh / Columns) keep the same `tertiary`
   background as the adjacent text buttons (Export / Import) — they are NOT
   bare icons. A slightly tighter horizontal padding makes them read as
   compact square buttons while still carrying the button surface. */
.t-list-shell__trailing-icon { padding-inline: 8px; }

/* Header-card search cluster (right of the page header bar). */
.t-list-shell__search { display: flex; align-items: center; gap: 8px; flex-wrap: nowrap; }
.t-list-shell__search--mobile { gap: 6px; }
.t-list-shell__search-input { width: 240px; max-width: 100%; }
.t-list-shell__adv-toggle :deep(.n-button__content) { color: var(--tnzi-base-text-muted, #6b7280); }
.t-list-shell__adv-toggle:hover :deep(.n-button__content) { color: var(--tnzi-primary); }

/* Phone downward-expanding search panel — one field per row, action buttons
   pinned right on their own row. Sits below the page-header bar, separated by
   a hairline so it reads as a distinct zone of the white header card. */
.t-list-shell__mobile-search {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--tnzi-border, #eef0f3);
}
.t-list-shell__mobile-field { width: 100%; }
.t-list-shell__mobile-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.t-list-shell__mobile-submit { min-width: 88px; }

@media (max-width: 640px) {
  .t-list-shell__footer { flex-direction: column; align-items: stretch; gap: 12px; }
  .t-list-shell__footer-left { justify-content: center; flex-wrap: wrap; }
  .t-list-shell__actions { flex-wrap: nowrap; overflow-x: auto; -webkit-overflow-scrolling: touch; scrollbar-width: none; }
  .t-list-shell__actions::-webkit-scrollbar { display: none; }
  .t-list-shell__search { flex-wrap: wrap; }
  .t-list-shell__search-input { width: 100%; }
}
</style>
