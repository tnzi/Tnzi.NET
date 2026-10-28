<script setup lang="ts">
/**
 * @experimental
 * TThreadList - the recent conversations section of a chat sidebar.
 *
 * A header that folds the list away (the label is the toggle; a chevron shows
 * on hover), with actions that appear on hover or keyboard focus: View all,
 * Search, and a menu to filter, group and sort. Then a row per conversation:
 * the title alone, and a "..." menu with rename, archive and delete.
 *
 * Filtering and ordering are the consumer's (`preferences` + `update:preferences`,
 * which `useChatThreads().recents` sends to the server so paging stays right);
 * grouping only arranges the rows given. Without `preferences` there is no
 * filter menu and no grouping, which is how the list looked before.
 *
 * Delete confirms **inline** rather than through a modal: a conversation list
 * is a place people delete things quickly, and a dialog per row would be
 * heavier than the action it guards. The confirmation is a single-value state
 * machine owned here (`confirmingId`), so at most one row is mid-confirm. The
 * same goes for renaming (`renamingId`).
 *
 * Row geometry deliberately matches `TSidebarNav`'s items (height, padding,
 * gap, radius, column inset), so the rows line up with the nav above. The rows
 * carry no icon: their titles start where the nav's icons do.
 */
import { computed, nextTick, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useDebounced } from '@tnzi/core/headless'
import { isImeComposing } from '@tnzi/core/utils'
import { useAiI18n } from '../../i18n/index'
import TActionMenu, { type ActionMenuItem } from '../overlay/TActionMenu.vue'
import {
  RECENTS_ACTIVITY_WINDOWS,
  RECENTS_GROUP_BYS,
  RECENTS_SORTS,
  RECENTS_STATUSES,
  formatRecentsMonth,
  groupThreadsByDate,
  type RecentsPreferences,
} from '../../headless/recents'

export interface ThreadItem {
  readonly id: string
  readonly title: string
  /** Last activity (ISO). */
  readonly updatedAt?: string
  /** Creation time (ISO); grouping by date uses it when the list is sorted by creation. */
  readonly createdAt?: string
  /** Archived conversations leave the Active list (see `RecentThreadsController`). */
  readonly archived?: boolean
  /**
   * An optimistic row for a conversation the backend has not committed yet
   * (`useChatThreads` adds one on the first turn). It IS the conversation on
   * screen, so it renders as the active row, and it has nothing server-side
   * to act on, so it has no actions menu.
   */
  readonly pending?: boolean
}

const props = withDefaults(
  defineProps<{
    threads?: ReadonlyArray<ThreadItem>
    activeThreadId?: string
    /**
     * A thread being opened (`useChatThreads().openingThreadId`): it takes the
     * highlight from `activeThreadId` at once, with a spinner at its end,
     * while its transcript loads.
     */
    openingThreadId?: string
    /** Section heading. Defaults to the current locale's; pass '' for none. */
    label?: string
    /** Delete in each row's menu, confirmed inline. */
    enableDelete?: boolean
    /** Inline confirm prompt. Defaults to the current locale's. */
    confirmLabel?: string
    /** Rename in each row's menu (`rename`). */
    enableRename?: boolean
    /** Archive / Unarchive in each row's menu (`archive` / `unarchive`). */
    enableArchive?: boolean
    /** A `+` (new chat) among the header actions. */
    showAddButton?: boolean
    /** A View all header action (`view-all`). */
    showViewAll?: boolean
    /**
     * A Search header action that opens a search box. Typing emits `search`
     * (debounced); the consumer filters (`useChatThreads().searchThreads`).
     */
    searchable?: boolean
    /**
     * The list's filter, order and grouping. Given, the header has a "Filter and
     * group" menu (`update:preferences` with the changed fields) and rows are
     * grouped as it says.
     */
    preferences?: RecentsPreferences
    /** Clicking the label folds the list away. */
    collapsible?: boolean
    /** Whether the list is folded away. Optional `v-model:collapsed`. */
    collapsed?: boolean
    /** More threads exist than the list shows: a "Show more" row emits `load-more`. */
    hasMore?: boolean
    /** A page is loading (the "Show more" row is disabled). */
    loadingMore?: boolean
  }>(),
  {
    threads: () => [],
    activeThreadId: undefined,
    openingThreadId: undefined,
    label: undefined,
    enableDelete: true,
    confirmLabel: undefined,
    enableRename: false,
    enableArchive: false,
    showAddButton: false,
    showViewAll: false,
    searchable: false,
    preferences: undefined,
    collapsible: true,
    collapsed: undefined,
    hasMore: false,
    loadingMore: false,
  },
)

const emit = defineEmits<{
  select: [threadId: string]
  delete: [threadId: string]
  rename: [threadId: string, title: string]
  archive: [threadId: string]
  unarchive: [threadId: string]
  add: []
  'view-all': []
  /** The search text changed ('' = cleared). Debounced. */
  search: [keyword: string]
  /** The user asked for the next page of threads. */
  'load-more': []
  /** Fields of `preferences` the user changed. */
  'update:preferences': [patch: Partial<RecentsPreferences>]
  'update:collapsed': [collapsed: boolean]
}>()

const t = useAiI18n()
const labelText = computed(() => props.label ?? t.value.chat.threadsLabel)
const confirmText = computed(() => props.confirmLabel ?? t.value.chat.deleteConfirm)

// ---------------------------------------------------------------- folding

const localCollapsed = ref(false)
const isCollapsed = computed(() => props.collapsible && (props.collapsed ?? localCollapsed.value))

function toggleCollapsed(): void {
  const next = !isCollapsed.value
  localCollapsed.value = next
  emit('update:collapsed', next)
}

// ---------------------------------------------------------------- search

const searchOpen = ref(false)
const keyword = ref('')
const searchInput = ref<HTMLInputElement | null>(null)
const emitSearch = useDebounced((value: string) => emit('search', value.trim()), 300)

function onSearchInput(event: Event): void {
  const value = (event.target as HTMLInputElement).value
  keyword.value = value
  emitSearch(value)
}

function clearSearch(): void {
  const had = keyword.value !== ''
  keyword.value = ''
  emitSearch.cancel()
  if (had) emit('search', '')
}

async function toggleSearch(): Promise<void> {
  if (searchOpen.value) {
    clearSearch()
    searchOpen.value = false
    return
  }
  searchOpen.value = true
  if (isCollapsed.value) toggleCollapsed()
  await nextTick()
  searchInput.value?.focus()
}

function onSearchKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || isImeComposing(event)) return
  /* A field that handles Escape claims it, so an overlay under the sidebar does not close too. */
  event.preventDefault()
  clearSearch()
  searchOpen.value = false
}

// ---------------------------------------------------------------- filter menu

const statusLabels = computed(() => ({
  active: t.value.recents.statusActive,
  archived: t.value.recents.statusArchived,
  all: t.value.recents.statusAll,
}))
const windowLabels = computed(() => ({
  '1d': t.value.recents.window1d,
  '3d': t.value.recents.window3d,
  '7d': t.value.recents.window7d,
  '30d': t.value.recents.window30d,
  all: t.value.recents.windowAll,
}))
const groupLabels = computed(() => ({ date: t.value.recents.groupDate, none: t.value.recents.groupNone }))
const sortLabels = computed(() => ({
  name: t.value.recents.sortName,
  created: t.value.recents.sortCreated,
  activity: t.value.recents.sortActivity,
}))

const filterItems = computed<ActionMenuItem[]>(() => {
  const p = props.preferences
  if (!p) return []
  return [
    {
      id: 'status',
      label: t.value.recents.status,
      value: statusLabels.value[p.status],
      children: RECENTS_STATUSES.map((s) => ({ id: `status:${s}`, label: statusLabels.value[s], checked: p.status === s })),
    },
    {
      id: 'lastActivity',
      label: t.value.recents.lastActivity,
      value: windowLabels.value[p.lastActivity],
      children: RECENTS_ACTIVITY_WINDOWS.map((w) => ({
        id: `lastActivity:${w}`,
        label: windowLabels.value[w],
        checked: p.lastActivity === w,
      })),
    },
    {
      id: 'groupBy',
      label: t.value.recents.groupBy,
      value: groupLabels.value[p.groupBy],
      dividerBefore: true,
      children: RECENTS_GROUP_BYS.map((g) => ({ id: `groupBy:${g}`, label: groupLabels.value[g], checked: p.groupBy === g })),
    },
    {
      id: 'sort',
      label: t.value.recents.sortBy,
      value: sortLabels.value[p.sort],
      children: RECENTS_SORTS.map((s) => ({ id: `sort:${s}`, label: sortLabels.value[s], checked: p.sort === s })),
    },
  ]
})

function onFilterSelect(id: string): void {
  const [field, value] = id.split(':')
  if (!field || !value) return
  emit('update:preferences', { [field]: value } as Partial<RecentsPreferences>)
}

const filterMenuOpen = ref(false)

// ---------------------------------------------------------------- rows

/* The row that reads as "the conversation you are in". A click moves it at
   once, before the transcript arrives; otherwise the previous row stayed lit
   for the whole load and the click looked ignored. The optimistic row of a
   new conversation is the one on screen until something else is opened. */
function isHighlighted(thread: ThreadItem): boolean {
  if (props.openingThreadId) return thread.id === props.openingThreadId
  return thread.id === props.activeThreadId || Boolean(thread.pending)
}

interface ThreadSection {
  readonly key: string
  readonly heading: string
  readonly threads: readonly ThreadItem[]
}

const sections = computed<ThreadSection[]>(() => {
  const p = props.preferences
  if (!p || p.groupBy !== 'date') return [{ key: 'all', heading: '', threads: props.threads }]
  const now = new Date()
  const headings: Record<string, string> = {
    today: t.value.recents.today,
    yesterday: t.value.recents.yesterday,
    previous7: t.value.recents.previous7,
    previous30: t.value.recents.previous30,
  }
  return groupThreadsByDate(props.threads, { now, field: p.sort === 'created' ? 'createdAt' : 'updatedAt' }).map((group) => ({
    key: group.key,
    heading: group.month ? formatRecentsMonth(group.month, now, t.value.recents.dateLocale) : headings[group.key] ?? '',
    threads: group.threads,
  }))
})

/* Something narrows the list, so an empty one means "nothing matches" rather
   than "no conversations yet" (a new account, where the section stays quiet). */
const isNarrowed = computed(() => {
  const p = props.preferences
  return Boolean(keyword.value) || (p !== undefined && (p.status !== 'active' || p.lastActivity !== 'all'))
})

function rowItems(thread: ThreadItem): ActionMenuItem[] {
  const items: ActionMenuItem[] = []
  if (props.enableRename) items.push({ id: 'rename', label: t.value.recents.rename, icon: 'lucide:pencil' })
  if (props.enableArchive) {
    items.push(
      thread.archived
        ? { id: 'unarchive', label: t.value.recents.unarchive, icon: 'lucide:archive-restore' }
        : { id: 'archive', label: t.value.recents.archive, icon: 'lucide:archive' },
    )
  }
  if (props.enableDelete) {
    items.push({ id: 'delete', label: t.value.recents.delete, icon: 'lucide:trash-2', danger: true, dividerBefore: items.length > 0 })
  }
  return items
}

const hasRowActions = computed(() => props.enableDelete || props.enableRename || props.enableArchive)

/* Only one row is ever mid-confirm or mid-rename; starting either on a row
   cancels it on any other. */
const confirmingId = ref<string | null>(null)
const renamingId = ref<string | null>(null)
const renameText = ref('')
let renameInput: HTMLInputElement | null = null
/** The row whose "..." menu is open, so its button stays shown. */
const menuOpenId = ref<string | null>(null)

function onRowMenuOpen(id: string, open: boolean): void {
  if (open) menuOpenId.value = id
  else if (menuOpenId.value === id) menuOpenId.value = null
}

function bindRenameInput(el: unknown): void {
  renameInput = el instanceof HTMLInputElement ? el : null
}

function onRowAction(thread: ThreadItem, action: string): void {
  switch (action) {
    case 'rename':
      void startRename(thread)
      break
    case 'archive':
      emit('archive', thread.id)
      break
    case 'unarchive':
      emit('unarchive', thread.id)
      break
    case 'delete':
      renamingId.value = null
      confirmingId.value = thread.id
      break
  }
}

async function startRename(thread: ThreadItem): Promise<void> {
  confirmingId.value = null
  renamingId.value = thread.id
  renameText.value = thread.title
  await nextTick()
  renameInput?.focus()
  renameInput?.select()
}

function commitRename(thread: ThreadItem): void {
  if (renamingId.value !== thread.id) return
  const title = renameText.value.trim()
  renamingId.value = null
  if (title && title !== thread.title) emit('rename', thread.id, title)
}

function onRenameKeydown(event: KeyboardEvent, thread: ThreadItem): void {
  if (isImeComposing(event)) return
  if (event.key === 'Enter') {
    event.preventDefault()
    commitRename(thread)
  } else if (event.key === 'Escape') {
    event.preventDefault()
    renamingId.value = null
  }
}

function confirmDelete(id: string): void {
  emit('delete', id)
  confirmingId.value = null
}

function cancelDelete(): void {
  confirmingId.value = null
}
</script>

<template>
  <div class="t-thread-list" :class="{ 'is-collapsed': isCollapsed }">
    <div
      v-if="labelText || showAddButton || showViewAll || searchable || preferences"
      class="t-thread-list__head"
      :class="{ 'is-engaged': filterMenuOpen || searchOpen }"
    >
      <button
        v-if="collapsible"
        type="button"
        class="t-thread-list__head-label"
        :aria-expanded="!isCollapsed"
        @click="toggleCollapsed"
      >
        <span>{{ labelText }}</span>
        <Icon
          :icon="isCollapsed ? 'lucide:chevron-right' : 'lucide:chevron-down'"
          class="t-thread-list__head-chevron"
          aria-hidden="true"
        />
      </button>
      <span v-else class="t-thread-list__head-text">{{ labelText }}</span>

      <div class="t-thread-list__head-actions">
        <button
          v-if="showViewAll"
          type="button"
          class="t-thread-list__head-act"
          :aria-label="t.recents.viewAll"
          :title="t.recents.viewAll"
          @click="emit('view-all')"
        >
          <Icon icon="lucide:arrow-up-right" />
        </button>
        <button
          v-if="searchable"
          type="button"
          class="t-thread-list__head-act"
          :class="{ 'is-on': searchOpen }"
          :aria-label="t.recents.search"
          :title="t.recents.search"
          :aria-pressed="searchOpen"
          @click="toggleSearch"
        >
          <Icon icon="lucide:search" />
        </button>
        <TActionMenu
          v-if="preferences"
          v-model:open="filterMenuOpen"
          :items="filterItems"
          :label="t.recents.filterAndGroup"
          :min-width="240"
          @select="onFilterSelect"
        >
          <template #trigger="{ toggle, attrs }">
            <button
              type="button"
              class="t-thread-list__head-act"
              :class="{ 'is-on': filterMenuOpen }"
              v-bind="attrs"
              :aria-label="t.recents.filterAndGroup"
              :title="t.recents.filterAndGroup"
              @click="toggle"
            >
              <Icon icon="lucide:sliders-horizontal" />
            </button>
          </template>
        </TActionMenu>
        <button
          v-if="showAddButton"
          type="button"
          class="t-thread-list__head-act"
          :aria-label="t.chat.newChat"
          :title="t.chat.newChat"
          @click="emit('add')"
        >
          <Icon icon="lucide:plus" />
        </button>
      </div>
    </div>

    <template v-if="!isCollapsed">
      <div v-if="searchable && searchOpen" class="t-thread-list__search">
        <Icon icon="lucide:search" class="t-thread-list__search-icon" aria-hidden="true" />
        <input
          ref="searchInput"
          :value="keyword"
          type="search"
          class="t-thread-list__search-input"
          :placeholder="t.chat.searchThreads"
          :aria-label="t.chat.searchThreads"
          @input="onSearchInput"
          @keydown="onSearchKeydown"
        />
      </div>
      <div v-if="isNarrowed && threads.length === 0" class="t-thread-list__empty">
        {{ t.chat.noThreadsFound }}
      </div>

      <template v-for="section in sections" :key="section.key">
        <div v-if="section.heading" class="t-thread-list__group">{{ section.heading }}</div>
        <div
          v-for="thread in section.threads"
          :key="thread.id"
          class="t-thread-list__row"
          :class="{
            'is-active': isHighlighted(thread),
            'is-opening': thread.id === openingThreadId,
            'is-menu-open': menuOpenId === thread.id,
          }"
        >
          <template v-if="confirmingId === thread.id">
            <span class="t-thread-list__confirm-label">{{ confirmText }}</span>
            <button
              type="button"
              class="t-thread-list__confirm t-thread-list__confirm--yes"
              @click="confirmDelete(thread.id)"
            >{{ t.common.yes }}</button>
            <button
              type="button"
              class="t-thread-list__confirm"
              @click="cancelDelete"
            >{{ t.common.no }}</button>
          </template>
          <input
            v-else-if="renamingId === thread.id"
            :ref="bindRenameInput"
            v-model="renameText"
            class="t-thread-list__rename"
            :aria-label="t.recents.rename"
            maxlength="200"
            @keydown="onRenameKeydown($event, thread)"
            @blur="commitRename(thread)"
          />
          <template v-else>
            <button
              type="button"
              class="t-thread-list__select"
              :aria-current="isHighlighted(thread) ? 'true' : undefined"
              :aria-busy="thread.id === openingThreadId ? 'true' : undefined"
              @click="emit('select', thread.id)"
            >
              <slot name="row" :thread="thread" :opening="thread.id === openingThreadId">
                <span class="t-thread-list__title">{{ thread.title }}</span>
              </slot>
            </button>
            <Icon
              v-if="thread.id === openingThreadId"
              icon="lucide:loader-circle"
              class="t-thread-list__spinner is-spinning"
              aria-hidden="true"
            />
            <TActionMenu
              v-else-if="hasRowActions && !thread.pending"
              :items="rowItems(thread)"
              :label="t.recents.moreActions"
              @update:open="(open) => onRowMenuOpen(thread.id, open)"
              @select="(action) => onRowAction(thread, action)"
            >
              <template #trigger="{ toggle, attrs }">
                <button
                  type="button"
                  class="t-thread-list__row-act"
                  v-bind="attrs"
                  :aria-label="t.recents.moreActions"
                  @click="toggle"
                >
                  <Icon icon="lucide:ellipsis" />
                </button>
              </template>
            </TActionMenu>
          </template>
        </div>
      </template>

      <button
        v-if="hasMore"
        type="button"
        class="t-thread-list__more"
        :disabled="loadingMore"
        @click="emit('load-more')"
      >
        <Icon :icon="loadingMore ? 'lucide:loader-circle' : 'lucide:chevron-down'" :class="{ 'is-spinning': loadingMore }" />
        <span>{{ t.chat.loadMoreThreads }}</span>
      </button>
    </template>

    <slot name="after" />
  </div>
</template>

<style scoped>
.t-thread-list {
  display: flex;
  flex-direction: column;
  gap: 1px;
  /* 8px, not 10px, to match `.t-sidebar-nav__group` - this is the column inset
     the alignment note above depends on. It was 10px, which put every thread
     row and the heading 2px right of the nav above them (measured: labels at
     49 vs 47, headings at 20 vs 18). */
  padding: 14px 8px 4px;
}
.t-thread-list__head {
  display: flex;
  align-items: center;
  gap: 4px;
  min-height: 28px;
  padding: 0 4px 2px 9px;
}
.t-thread-list__head-label,
.t-thread-list__head-text {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  margin-right: auto;
  padding: 0;
  border: none;
  background: none;
  color: var(--tnzi-ai-text-tertiary);
  font-family: inherit;
  font-size: 13px;
  font-weight: 400;
  line-height: 20px;
  text-align: left;
}
.t-thread-list__head-label {
  cursor: pointer;
  border-radius: 4px;
}
.t-thread-list__head-label > span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-thread-list__head-label:hover,
.t-thread-list__head-label:focus-visible {
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-list__head-chevron {
  flex-shrink: 0;
  width: 14px;
  height: 14px;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
/* The header's chevron and actions are hidden until the header is hovered or
   holds keyboard focus: a sidebar section should not be a row of buttons. A
   folded list keeps its chevron, the only sign there is something to unfold. */
.t-thread-list__head:hover .t-thread-list__head-chevron,
.t-thread-list__head-label:focus-visible .t-thread-list__head-chevron,
.t-thread-list.is-collapsed .t-thread-list__head-chevron {
  opacity: 1;
}
.t-thread-list__head-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-list__head:hover .t-thread-list__head-actions,
.t-thread-list__head:focus-within .t-thread-list__head-actions,
.t-thread-list__head.is-engaged .t-thread-list__head-actions {
  opacity: 1;
}
.t-thread-list__head-act {
  width: 24px;
  height: 24px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 15px;
}
.t-thread-list__head-act:hover,
.t-thread-list__head-act.is-on {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-thread-list__search {
  position: relative;
  margin: 2px 0 6px;
}
.t-thread-list__search-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--tnzi-ai-text-tertiary);
  font-size: 14px;
  pointer-events: none;
}
.t-thread-list__search-input {
  width: 100%;
  height: 32px;
  padding: 0 10px 0 30px;
  border-radius: 8px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 13px;
  outline: none;
}
.t-thread-list__search-input:focus { border-color: var(--tnzi-ai-border-strong); }
.t-thread-list__empty {
  padding: 6px 9px;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-thread-list__group {
  padding: 10px 9px 4px;
  font-size: 12px;
  line-height: 16px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-thread-list__more {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  padding: 0 9px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 13px;
  cursor: pointer;
}
.t-thread-list__more:hover:not(:disabled) { background: var(--tnzi-ai-hover); }
.t-thread-list__more:disabled { cursor: default; opacity: 0.7; }
.t-thread-list__more .is-spinning,
.t-thread-list__spinner.is-spinning { animation: t-thread-list-spin 1s linear infinite; }
@keyframes t-thread-list-spin { to { transform: rotate(360deg); } }

.t-thread-list__row {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  /* Row tokens: a shell sets touch sizes on a phone (see TChatApp). */
  height: var(--tnzi-ai-row-height, 32px);
  padding: 0 9px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text-secondary);
  font-family: inherit;
  font-size: var(--tnzi-ai-row-font-size, 14px);
  cursor: pointer;
  text-align: left;
  min-width: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-list__row:hover,
.t-thread-list__row.is-menu-open { background: var(--tnzi-ai-hover); }
/* The open conversation is marked by its background alone, as the nav rows
   above it are: no edge bar. */
.t-thread-list__row.is-active {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent-text);
  font-weight: 500;
}
.t-thread-list__select {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
  min-width: 0;
  height: 100%;
  border: none;
  background: none;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  padding: 0;
}
/* Hidden until the row is hovered or something in it has keyboard focus, so a
   long list is not a column of buttons; on a touch screen there is no hover,
   so it always shows. */
.t-thread-list__row-act {
  flex-shrink: 0;
  width: var(--tnzi-ai-row-action-size, 24px);
  height: var(--tnzi-ai-row-action-size, 24px);
  margin-right: -5px;
  border: none;
  background: none;
  color: var(--tnzi-ai-text-tertiary);
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-list__row:hover .t-thread-list__row-act,
.t-thread-list__row:focus-within .t-thread-list__row-act,
.t-thread-list__row.is-menu-open .t-thread-list__row-act {
  opacity: 1;
}
@media (hover: none) {
  .t-thread-list__row-act,
  .t-thread-list__head-actions,
  .t-thread-list__head-chevron {
    opacity: 1;
  }
}
.t-thread-list__row-act:hover,
.t-thread-list__row.is-menu-open .t-thread-list__row-act {
  background: var(--tnzi-ai-press);
  color: var(--tnzi-ai-text);
}
.t-thread-list__spinner {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-thread-list__rename {
  flex: 1;
  min-width: 0;
  height: 26px;
  margin: 0 -6px;
  padding: 0 6px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 6px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 14px;
  font-weight: 400;
  outline: none;
}
.t-thread-list__confirm-label {
  flex: 1;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-thread-list__confirm {
  flex-shrink: 0;
  border: none;
  background: none;
  font-size: 11px;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
  cursor: pointer;
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-list__confirm:hover {
  background: var(--tnzi-ai-hover);
}
.t-thread-list__confirm--yes {
  color: var(--tnzi-ai-danger);
}
.t-thread-list__confirm--yes:hover {
  background: var(--tnzi-ai-danger-soft);
}
.t-thread-list__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
