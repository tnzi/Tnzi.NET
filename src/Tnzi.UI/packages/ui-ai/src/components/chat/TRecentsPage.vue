<script setup lang="ts">
/**
 * @experimental
 * TRecentsPage - every conversation, on a page of its own: a heading, Active
 * and Archived tabs, a search, then one row per conversation with when it was
 * last active ("5 hours ago", "4 days ago", then a date), in pages.
 *
 * `TChatApp` shows it for `view === 'recents'` (the sidebar's View all) when it
 * has a `recents` controller; use it directly in a page of your own the same
 * way. The data and the actions come from the controller
 * (`useChatThreads().recents`); opening and deleting go out as events, because
 * those also change the conversation on screen, which the shell owns.
 *
 * Rows open on click; "..." holds rename (in place), archive / unarchive and
 * delete (confirmed in place, as in the sidebar).
 */
import { computed, nextTick, onMounted, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useDebounced } from '@tnzi/core/headless'
import { isImeComposing } from '@tnzi/core/utils'
import { useAiI18n } from '../../i18n/index'
import TActionMenu, { type ActionMenuItem } from '../overlay/TActionMenu.vue'
import { formatRecentActivity, type RecentThreadsController } from '../../headless/recents'
import type { ThreadItem } from './TThreadList.vue'

const props = withDefaults(
  defineProps<{
    controller: RecentThreadsController
    /** Page heading. Defaults to the current locale's conversation-list label. */
    title?: string
    /** The conversation on screen, marked in the list. */
    activeThreadId?: string
    /**
     * Leave out conversations the product lists elsewhere (the same predicate it
     * applies to the sidebar's `threads`).
     */
    include?: (thread: ThreadItem) => boolean
    /** Delete in each row's menu, confirmed in place. */
    enableDelete?: boolean
    /** Inline delete prompt. Defaults to the current locale's. */
    confirmLabel?: string
  }>(),
  {
    title: undefined,
    activeThreadId: undefined,
    include: undefined,
    enableDelete: true,
    confirmLabel: undefined,
  },
)

const emit = defineEmits<{
  /** Open a conversation. */
  select: [threadId: string]
  /** Delete a conversation (after the inline confirmation). */
  delete: [threadId: string]
}>()

const t = useAiI18n()
const page = props.controller.page

const heading = computed(() => props.title ?? t.value.chat.threadsLabel)
const confirmText = computed(() => props.confirmLabel ?? t.value.chat.deleteConfirm)
const rows = computed(() => (props.include ? page.threads.value.filter(props.include) : page.threads.value))

onMounted(() => {
  void page.load()
})

// ---------------------------------------------------------------- tabs + search

const TABS = ['active', 'archived'] as const
const tabLabel = (tab: (typeof TABS)[number]) => (tab === 'active' ? t.value.recents.statusActive : t.value.recents.statusArchived)

function onTabKeydown(event: KeyboardEvent): void {
  if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
  event.preventDefault()
  const next = page.status.value === 'active' ? 'archived' : 'active'
  void page.setStatus(next)
  void nextTick(() => document.getElementById(`t-recents-tab-${next}`)?.focus())
}

const searchOpen = ref(page.keyword.value !== '')
const keyword = ref(page.keyword.value)
const searchInput = ref<HTMLInputElement | null>(null)
const runSearch = useDebounced((value: string) => void page.search(value.trim()), 300)

function onSearchInput(event: Event): void {
  keyword.value = (event.target as HTMLInputElement).value
  runSearch(keyword.value)
}

async function toggleSearch(): Promise<void> {
  if (searchOpen.value) {
    closeSearch()
    return
  }
  searchOpen.value = true
  await nextTick()
  searchInput.value?.focus()
}

const searchButton = ref<HTMLButtonElement | null>(null)

/* Clearing collapses the field back into its button, and focus goes to that
   button: the field it was in no longer exists. */
async function closeSearch(): Promise<void> {
  runSearch.cancel()
  const had = keyword.value !== '' || page.keyword.value !== ''
  keyword.value = ''
  searchOpen.value = false
  if (had) void page.search('')
  await nextTick()
  searchButton.value?.focus()
}

function onSearchKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || isImeComposing(event)) return
  event.preventDefault()
  void closeSearch()
}

const emptyText = computed(() => {
  if (page.keyword.value) return t.value.chat.noThreadsFound
  return page.status.value === 'archived' ? t.value.recents.emptyArchived : t.value.recents.emptyActive
})

// ---------------------------------------------------------------- rows

const now = ref(new Date())
const activity = (thread: ThreadItem) => formatRecentActivity(thread.updatedAt, now.value, t.value.recents.dateLocale)

function rowItems(thread: ThreadItem): ActionMenuItem[] {
  const items: ActionMenuItem[] = [
    { id: 'rename', label: t.value.recents.rename, icon: 'lucide:pencil' },
    thread.archived || page.status.value === 'archived'
      ? { id: 'unarchive', label: t.value.recents.unarchive, icon: 'lucide:archive-restore' }
      : { id: 'archive', label: t.value.recents.archive, icon: 'lucide:archive' },
  ]
  if (props.enableDelete) {
    items.push({ id: 'delete', label: t.value.recents.delete, icon: 'lucide:trash-2', danger: true, dividerBefore: true })
  }
  return items
}

const confirmingId = ref<string | null>(null)
const renamingId = ref<string | null>(null)
const renameText = ref('')
const menuOpenId = ref<string | null>(null)
let renameInput: HTMLInputElement | null = null

function bindRenameInput(el: unknown): void {
  renameInput = el instanceof HTMLInputElement ? el : null
}

function onRowMenuOpen(id: string, open: boolean): void {
  if (open) menuOpenId.value = id
  else if (menuOpenId.value === id) menuOpenId.value = null
}

async function onRowAction(thread: ThreadItem, action: string): Promise<void> {
  if (action === 'rename') {
    confirmingId.value = null
    renamingId.value = thread.id
    renameText.value = thread.title
    await nextTick()
    renameInput?.focus()
    renameInput?.select()
  } else if (action === 'archive') {
    await props.controller.archive(thread.id)
  } else if (action === 'unarchive') {
    await props.controller.unarchive(thread.id)
  } else if (action === 'delete') {
    renamingId.value = null
    confirmingId.value = thread.id
  }
}

function commitRename(thread: ThreadItem): void {
  if (renamingId.value !== thread.id) return
  const title = renameText.value.trim()
  renamingId.value = null
  if (title && title !== thread.title) void props.controller.rename(thread.id, title)
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
  confirmingId.value = null
  emit('delete', id)
}
</script>

<template>
  <div class="t-recents-page">
    <div class="t-recents-page__col">
      <h1 class="t-recents-page__heading">{{ heading }}</h1>

      <div class="t-recents-page__bar">
        <div class="t-recents-page__tabs" role="tablist" :aria-label="t.recents.tabs">
          <button
            v-for="tab in TABS"
            :id="`t-recents-tab-${tab}`"
            :key="tab"
            type="button"
            role="tab"
            class="t-recents-page__tab"
            :class="{ 'is-active': page.status.value === tab }"
            :aria-selected="page.status.value === tab"
            :tabindex="page.status.value === tab ? 0 : -1"
            @click="page.setStatus(tab)"
            @keydown="onTabKeydown"
          >{{ tabLabel(tab) }}</button>
        </div>
        <!-- The search expands in place, in this row, rather than adding a row
             under it: the button becomes the field and the field's x turns it
             back into the button. -->
        <div v-if="searchOpen" class="t-recents-page__search" role="search">
          <Icon icon="lucide:search" class="t-recents-page__search-icon" aria-hidden="true" />
          <input
            ref="searchInput"
            :value="keyword"
            type="text"
            class="t-recents-page__search-input"
            :placeholder="t.recents.searchPlaceholder"
            :aria-label="t.chat.searchThreads"
            @input="onSearchInput"
            @keydown="onSearchKeydown"
          />
          <button
            type="button"
            class="t-recents-page__search-close"
            :aria-label="t.recents.closeSearch"
            :title="t.recents.closeSearch"
            @click="closeSearch"
          >
            <Icon icon="lucide:x" />
          </button>
        </div>
        <button
          v-else
          ref="searchButton"
          type="button"
          class="t-recents-page__icon-btn"
          :aria-label="t.recents.search"
          :title="t.recents.search"
          @click="toggleSearch"
        >
          <Icon icon="lucide:search" />
        </button>
      </div>

      <div v-if="page.loading.value && rows.length === 0" class="t-recents-page__state" aria-busy="true">
        <Icon icon="lucide:loader-circle" class="is-spinning" aria-hidden="true" />
      </div>
      <div v-else-if="page.loaded.value && rows.length === 0" class="t-recents-page__state">
        {{ emptyText }}
      </div>

      <ul v-else class="t-recents-page__list">
        <li
          v-for="thread in rows"
          :key="thread.id"
          class="t-recents-page__row"
          :class="{ 'is-current': thread.id === activeThreadId, 'is-menu-open': menuOpenId === thread.id }"
        >
          <template v-if="confirmingId === thread.id">
            <span class="t-recents-page__confirm-label">{{ confirmText }}</span>
            <button type="button" class="t-recents-page__confirm t-recents-page__confirm--yes" @click="confirmDelete(thread.id)">
              {{ t.common.yes }}
            </button>
            <button type="button" class="t-recents-page__confirm" @click="confirmingId = null">{{ t.common.no }}</button>
          </template>
          <input
            v-else-if="renamingId === thread.id"
            :ref="bindRenameInput"
            v-model="renameText"
            class="t-recents-page__rename"
            :aria-label="t.recents.rename"
            maxlength="200"
            @keydown="onRenameKeydown($event, thread)"
            @blur="commitRename(thread)"
          />
          <template v-else>
            <button
              type="button"
              class="t-recents-page__open"
              :aria-current="thread.id === activeThreadId ? 'true' : undefined"
              @click="emit('select', thread.id)"
            >
              <span class="t-recents-page__title">{{ thread.title }}</span>
              <span class="t-recents-page__time">{{ activity(thread) }}</span>
            </button>
            <TActionMenu
              :items="rowItems(thread)"
              :label="t.recents.moreActions"
              @update:open="(open) => onRowMenuOpen(thread.id, open)"
              @select="(action) => onRowAction(thread, action)"
            >
              <template #trigger="{ toggle, attrs }">
                <button
                  type="button"
                  class="t-recents-page__row-act"
                  v-bind="attrs"
                  :aria-label="t.recents.moreActions"
                  @click="toggle"
                >
                  <Icon icon="lucide:ellipsis" />
                </button>
              </template>
            </TActionMenu>
          </template>
        </li>
      </ul>

      <button
        v-if="page.hasMore.value"
        type="button"
        class="t-recents-page__more"
        :disabled="page.loadingMore.value"
        @click="page.loadMore()"
      >
        <Icon :icon="page.loadingMore.value ? 'lucide:loader-circle' : 'lucide:chevron-down'" :class="{ 'is-spinning': page.loadingMore.value }" />
        <span>{{ t.chat.loadMoreThreads }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
/* No scroller of its own: the region it is placed in scrolls (`TChatApp`'s
   view area does). */
.t-recents-page {
  color: var(--tnzi-ai-text);
}
.t-recents-page__col {
  max-width: 768px;
  margin: 0 auto;
  padding: 40px 24px 48px;
}
.t-recents-page__heading {
  margin: 0 0 24px;
  font-family: var(--tnzi-ai-font-display, serif);
  font-size: 30px;
  font-weight: 400;
  line-height: 1.2;
  color: var(--tnzi-ai-text);
}
.t-recents-page__bar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}
.t-recents-page__tabs {
  display: flex;
  gap: 4px;
  margin-right: auto;
}
.t-recents-page__tab {
  height: 32px;
  padding: 0 12px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 14px;
  cursor: pointer;
}
.t-recents-page__tab:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }
.t-recents-page__tab.is-active {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
  font-weight: 500;
}
.t-recents-page__icon-btn {
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  cursor: pointer;
}
.t-recents-page__icon-btn:hover,
.t-recents-page__icon-btn.is-on { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }
/* The field takes up to 384px of the row and gives way to the tabs on a narrow
   screen (`min-width: 0`), so the row never wraps. */
.t-recents-page__search {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 0 1 384px;
  min-width: 0;
  height: 32px;
  box-sizing: border-box;
  padding: 0 5px 0 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: color-mix(in srgb, var(--tnzi-ai-surface) 70%, transparent);
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    box-shadow var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-recents-page__search:focus-within {
  border-color: var(--tnzi-ai-accent);
  box-shadow: 0 0 0 3px var(--tnzi-ai-accent-soft);
}
.t-recents-page__search-icon {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-recents-page__search-input {
  flex: 1;
  min-width: 0;
  height: 100%;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 14px;
  outline: none;
}
.t-recents-page__search-input::placeholder { color: var(--tnzi-ai-text-tertiary); }
.t-recents-page__search-close {
  flex-shrink: 0;
  width: 22px;
  height: 22px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  cursor: pointer;
}
.t-recents-page__search-close:hover,
.t-recents-page__search-close:focus-visible {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-recents-page__state {
  display: flex;
  justify-content: center;
  padding: 48px 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-recents-page__list {
  margin: 0;
  padding: 0;
  list-style: none;
}
/* One line per conversation: title on the left, when it was last active on the
   right. Rows are separated by hairlines rather than boxed, as a list of
   documents; the hairline is drawn inset so the 8px hover shape can round its
   corners without bending it. */
.t-recents-page__row {
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 53px;
  padding: 0 8px 0 12px;
  border-radius: 8px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-recents-page__row::after {
  content: '';
  position: absolute;
  left: 12px;
  right: 8px;
  bottom: 0;
  height: 1px;
  background: var(--tnzi-ai-divider, #ebebeb);
}
.t-recents-page__row:hover,
.t-recents-page__row.is-menu-open { background: var(--tnzi-ai-hover); }
.t-recents-page__row:hover::after,
.t-recents-page__row.is-menu-open::after { background: transparent; }
.t-recents-page__open {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 15px 0;
  border: none;
  background: none;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
}
.t-recents-page__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 15px;
  color: var(--tnzi-ai-text);
}
.t-recents-page__row.is-current .t-recents-page__title { font-weight: 500; }
.t-recents-page__time {
  flex-shrink: 0;
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
  white-space: nowrap;
}
.t-recents-page__row-act {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  border: none;
  border-radius: 6px;
  background: none;
  color: var(--tnzi-ai-text-tertiary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  cursor: pointer;
  opacity: 0;
}
.t-recents-page__row:hover .t-recents-page__row-act,
.t-recents-page__row:focus-within .t-recents-page__row-act,
.t-recents-page__row.is-menu-open .t-recents-page__row-act { opacity: 1; }
@media (hover: none) {
  .t-recents-page__row-act { opacity: 1; }
}
.t-recents-page__row-act:hover,
.t-recents-page__row.is-menu-open .t-recents-page__row-act {
  background: var(--tnzi-ai-press);
  color: var(--tnzi-ai-text);
}
.t-recents-page__rename {
  flex: 1;
  min-width: 0;
  height: 36px;
  margin: 8px 0;
  padding: 0 8px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font: inherit;
  font-size: 15px;
  outline: none;
}
.t-recents-page__confirm-label {
  flex: 1;
  font-size: 14px;
  color: var(--tnzi-ai-text-secondary);
}
.t-recents-page__confirm {
  flex-shrink: 0;
  height: 28px;
  padding: 0 10px;
  border: none;
  border-radius: 6px;
  background: none;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}
.t-recents-page__confirm:hover { background: var(--tnzi-ai-hover); }
.t-recents-page__confirm--yes { color: var(--tnzi-ai-danger); }
.t-recents-page__confirm--yes:hover { background: var(--tnzi-ai-danger-soft); }
.t-recents-page__more {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 36px;
  margin: 12px auto 0;
  padding: 0 14px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 14px;
  cursor: pointer;
}
.t-recents-page__more:hover:not(:disabled) { background: var(--tnzi-ai-hover); }
.t-recents-page__more:disabled { cursor: default; opacity: 0.7; }
.is-spinning { animation: t-recents-spin 1s linear infinite; }
@keyframes t-recents-spin { to { transform: rotate(360deg); } }
@media (max-width: 640px) {
  .t-recents-page__col { padding: 24px 16px 32px; }
  .t-recents-page__heading { font-size: 24px; margin-bottom: 16px; }
  /* Still one line on a phone: the title keeps the room and ellipsises, the
     time is a size smaller. Two lines per row halved what fits on a screen. */
  .t-recents-page__open { gap: 12px; }
  .t-recents-page__time { font-size: 12px; }
}
</style>
