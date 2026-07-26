<script setup lang="ts" generic="T = unknown">
/**
 * `TWidgetList` - paginated dashboard list card.
 *
 * The "list" primitive of the Workbench widget family. Renders a bounded,
 * page-level-paginated list of arbitrary rows so a list panel occupies a
 * FIXED height (equal across side-by-side cards) instead of growing with
 * its item count. Each row's content is supplied via the `#row` slot; the
 * list chrome (click target, dividers, empty state, compact prev/next
 * pager) is uniform.
 *
 * Two shapes:
 *   - **Standalone card** (default) - renders its own NCard + header
 *     (title + optional link). Drop it straight into a custom dashboard:
 *     ```vue
 *     <TWidgetList
 *       :title="t('panels.myFiles')" :items="matters" :page-size="5"
 *       :link-text="t('links.all')" @link="goAll" @row-click="openMatter"
 *     >
 *       <template #row="{ item }">…</template>
 *     </TWidgetList>
 *     ```
 *   - **Bare** (`bare`) - strips the NCard/header so it nests inside a
 *     `TWidgetCard` (WidgetDef grid), which then owns the title + refresh.
 *
 * Generalised from the "recent files / my deadlines / review queue" list
 * card that consumer apps kept hand-rolling, so it ships once in the
 * framework instead of once per app.
 */
import { ref, computed, watch } from 'vue'
import { NCard } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TEmpty from '../../components/data/TEmpty.vue'
import { maybeTranslate } from '../../pages/_shared/translate'

export type WidgetListTone = 'default' | 'primary'

const props = withDefaults(
  defineProps<{
    /** Rows to render. Sliced to the current page internally. */
    items: T[]
    /** Rows per page. The card height is bounded to this many rows. Default 5. */
    pageSize?: number
    /** Header title - i18n key (resolved against the bundled locale) or raw text. */
    title?: string
    /** Iconify icon shown before the title. */
    icon?: string
    /** Title colour. `primary` tints it with the brand colour. Default `default`. */
    tone?: WidgetListTone
    /** Right-aligned header link text - i18n key or raw. Emits `link` on click. */
    linkText?: string
    /** Empty-state copy - i18n key or raw. */
    emptyText?: string
    /** Empty-state icon. Default `mdi:inbox-outline`. */
    emptyIcon?: string
    /** Rows are click targets (emit `row-click`). Default true. */
    clickable?: boolean
    /** Stretch to the container height (2-up equal-height rows / fill boards). Default false. */
    fill?: boolean
    /** Strip the NCard + header so the panel nests inside a TWidgetCard. Default false. */
    bare?: boolean
    /** Stable row key. Defaults to `item.id ?? index`. */
    rowKey?: (item: T, index: number) => string | number
  }>(),
  {
    pageSize: 5,
    title: undefined,
    icon: undefined,
    tone: 'default',
    linkText: undefined,
    emptyText: undefined,
    emptyIcon: 'mdi:inbox-outline',
    clickable: true,
    fill: false,
    bare: false,
    rowKey: undefined,
  },
)

const emit = defineEmits<{
  (e: 'link'): void
  (e: 'row-click', item: T, index: number): void
}>()

defineSlots<{
  /** Row body. Receives the item + its absolute index in `items`. */
  row?: (props: { item: T; index: number }) => unknown
  /** Override the empty state entirely. */
  empty?: () => unknown
  /** Extra header content (right of the title, left of the link). */
  'header-extra'?: () => unknown
}>()

const page = ref(1)
const pageCount = computed(() => Math.max(1, Math.ceil((props.items?.length ?? 0) / props.pageSize)))
const pageStart = computed(() => (page.value - 1) * props.pageSize)
const paged = computed(() => (props.items ?? []).slice(pageStart.value, pageStart.value + props.pageSize))

function keyOf(item: T, index: number): string | number {
  if (props.rowKey) return props.rowKey(item, index)
  const id = (item as { id?: string | number } | null)?.id
  return id ?? index
}
/** Absolute index in `items` for a row on the current page (for `#row` / emits). */
function absIndex(localIndex: number): number {
  return pageStart.value + localIndex
}

const resolvedTitle = computed(() => maybeTranslate(props.title))
const resolvedLink = computed(() => maybeTranslate(props.linkText))
const resolvedEmpty = computed(() => maybeTranslate(props.emptyText))

const isEmpty = computed(() => !props.items || props.items.length === 0)
const showHeader = computed(() => !!resolvedTitle.value || !!resolvedLink.value || false)

// New data (filter / refresh) resets to the first page; clamp if the list shrank.
watch(
  () => props.items,
  () => {
    page.value = 1
  },
)
watch(pageCount, (n) => {
  if (page.value > n) page.value = n
})
</script>

<template>
  <!-- Bare: chrome-less body for nesting inside a TWidgetCard. -->
  <div v-if="bare" class="t-widget-list t-widget-list--bare" :class="{ 't-widget-list--fill': fill }">
    <slot v-if="isEmpty" name="empty">
      <TEmpty :text="resolvedEmpty" :icon="emptyIcon" size="small" class="t-widget-list__empty" />
    </slot>
    <template v-else>
      <ul class="t-widget-list__ul">
        <li
          v-for="(item, i) in paged"
          :key="keyOf(item, i)"
          class="t-widget-list__row"
          :class="{ 't-widget-list__row--clickable': clickable }"
          @click="clickable && emit('row-click', item, absIndex(i))"
        >
          <slot name="row" :item="item" :index="absIndex(i)" />
        </li>
      </ul>
      <div v-if="pageCount > 1" class="t-widget-list__pager">
        <button
          class="t-widget-list__pager-btn"
          type="button"
          :disabled="page === 1"
          aria-label="Previous"
          @click="page--"
        >
          <TSvgIcon icon="mdi:chevron-left" :size="16" />
        </button>
        <span class="t-widget-list__pager-info">{{ page }} / {{ pageCount }}</span>
        <button
          class="t-widget-list__pager-btn"
          type="button"
          :disabled="page === pageCount"
          aria-label="Next"
          @click="page++"
        >
          <TSvgIcon icon="mdi:chevron-right" :size="16" />
        </button>
      </div>
    </template>
  </div>

  <!-- Standalone card. -->
  <NCard v-else class="t-widget-list" :class="{ 't-widget-list--fill': fill }" :bordered="false">
    <template v-if="showHeader" #header>
      <div class="t-widget-list__head" :class="`t-widget-list__head--${tone}`">
        <TSvgIcon v-if="icon" :icon="icon" :size="16" class="t-widget-list__head-icon" />
        <span class="t-widget-list__head-title">{{ resolvedTitle }}</span>
      </div>
    </template>
    <template v-if="showHeader && (resolvedLink || $slots['header-extra'])" #header-extra>
      <div class="t-widget-list__head-extra">
        <slot name="header-extra" />
        <a v-if="resolvedLink" class="t-widget-list__link" @click="emit('link')">{{ resolvedLink }}</a>
      </div>
    </template>

    <slot v-if="isEmpty" name="empty">
      <TEmpty :text="resolvedEmpty" :icon="emptyIcon" size="small" class="t-widget-list__empty" />
    </slot>
    <template v-else>
      <ul class="t-widget-list__ul">
        <li
          v-for="(item, i) in paged"
          :key="keyOf(item, i)"
          class="t-widget-list__row"
          :class="{ 't-widget-list__row--clickable': clickable }"
          @click="clickable && emit('row-click', item, absIndex(i))"
        >
          <slot name="row" :item="item" :index="absIndex(i)" />
        </li>
      </ul>
      <div v-if="pageCount > 1" class="t-widget-list__pager">
        <button
          class="t-widget-list__pager-btn"
          type="button"
          :disabled="page === 1"
          aria-label="Previous"
          @click="page--"
        >
          <TSvgIcon icon="mdi:chevron-left" :size="16" />
        </button>
        <span class="t-widget-list__pager-info">{{ page }} / {{ pageCount }}</span>
        <button
          class="t-widget-list__pager-btn"
          type="button"
          :disabled="page === pageCount"
          aria-label="Next"
          @click="page++"
        >
          <TSvgIcon icon="mdi:chevron-right" :size="16" />
        </button>
      </div>
    </template>
  </NCard>
</template>

<style scoped>
.t-widget-list {
  border-radius: var(--tnzi-admin-radius-md, 8px);
}
/* Standalone card only - the NCard carries the soft dashboard shadow. */
.t-widget-list:not(.t-widget-list--bare) {
  box-shadow: var(--tnzi-shadow-card, 0 1px 2px rgb(0 0 0 / 0.05));
}
.t-widget-list--fill {
  height: 100%;
}
.t-widget-list :deep(.n-card__content) {
  display: flex;
  flex-direction: column;
}
.t-widget-list--fill :deep(.n-card__content) {
  height: 100%;
}
/* Bare mode fills the surrounding TWidgetCard body. */
.t-widget-list--bare {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}

.t-widget-list__head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  font-weight: 700;
  font-size: 15px;
  color: var(--tnzi-base-text);
}
.t-widget-list__head--primary,
.t-widget-list__head--primary .t-widget-list__head-icon {
  color: var(--tnzi-primary);
}
.t-widget-list__head-icon {
  flex-shrink: 0;
  color: var(--tnzi-primary);
}
.t-widget-list__head-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-widget-list__head-extra {
  display: flex;
  align-items: center;
  gap: 10px;
}
.t-widget-list__link {
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-base-text-muted);
  cursor: pointer;
}
.t-widget-list__link:hover {
  color: var(--tnzi-primary);
}

.t-widget-list__ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
}
.t-widget-list__row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 11px 0;
  border-top: 1px solid var(--tnzi-border);
}
.t-widget-list__row:first-child {
  border-top: none;
}
.t-widget-list__row--clickable {
  cursor: pointer;
}
.t-widget-list__empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px 0;
}

.t-widget-list__pager {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  margin-top: auto;
  padding-top: 10px;
  border-top: 1px solid var(--tnzi-border);
}
.t-widget-list__pager-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 6px;
  border: 1px solid var(--tnzi-border);
  background: transparent;
  color: var(--tnzi-base-text);
  cursor: pointer;
  transition:
    border-color 0.15s,
    color 0.15s;
}
.t-widget-list__pager-btn:hover:not(:disabled) {
  border-color: var(--tnzi-primary);
  color: var(--tnzi-primary);
}
.t-widget-list__pager-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.t-widget-list__pager-info {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
  min-width: 34px;
  text-align: center;
}
</style>
