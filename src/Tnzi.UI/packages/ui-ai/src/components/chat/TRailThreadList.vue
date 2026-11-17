<script setup lang="ts">
/**
 * @experimental
 * TRailThreadList - the recent conversations, as a list inside a rail flyout.
 *
 * What the collapsed rail offers instead of the expanded sidebar's
 * `TThreadList`: titles only, the open conversation marked, a way to the full
 * list when there is one, and a sentence instead of an empty box when there is
 * nothing to list. No row actions (rename, archive, delete stay in the
 * expanded sidebar and on the Recents page); the flyout is for switching.
 *
 * `TChatApp` renders it in its rail with `threads`. A product whose list is
 * something else (its own sessions) can render it in the `#rail-threads` slot
 * with items of the same shape, `{ id, title }`.
 *
 * `threadIcon` puts the same origin icon at a row's end as the expanded sidebar
 * does; every title starts at the same x. There is no "..." here, so the icon
 * stays on hover. A conversation that is running or has an unread reply shows
 * that there instead (`TThreadStatus`), as in the sidebar.
 */
import { computed } from 'vue'
import { useAiI18n } from '../../i18n/index'
import TThreadIcon from './TThreadIcon.vue'
import TThreadStatus from './TThreadStatus.vue'
import { resolveThreadIcon, type ThreadIcon, type ThreadIconResolver } from './thread-icon'

export interface RailThreadItem {
  readonly id: string
  readonly title: string
  /** See `ThreadItem.origin`. */
  readonly origin?: string
  /** See `ThreadItem.awaitingOwner`. */
  readonly awaitingOwner?: boolean
  /** See `ThreadItem.running`. */
  readonly running?: boolean
  /** See `ThreadItem.unread`. */
  readonly unread?: boolean
}

const props = withDefaults(
  defineProps<{
    threads?: ReadonlyArray<RailThreadItem>
    /** The open conversation, marked with `aria-current` and the selected fill. */
    activeThreadId?: string | null
    /** A conversation being opened, marked like the open one until it is. */
    openingThreadId?: string | null
    /** Offer a "View all" row at the end. */
    showViewAll?: boolean
    /** Text shown when `threads` is empty. Defaults to the locale's "No conversations yet." */
    emptyLabel?: string
    /** Text of the "View all" row. Defaults to the locale's. */
    viewAllLabel?: string
    /** Maps a thread to the icon before its title, or null for none (see `TThreadList`). */
    threadIcon?: ThreadIconResolver
  }>(),
  {
    threads: () => [],
    activeThreadId: null,
    openingThreadId: null,
    showViewAll: false,
    emptyLabel: undefined,
    viewAllLabel: undefined,
    threadIcon: undefined,
  },
)

const emit = defineEmits<{
  select: [threadId: string]
  'view-all': []
}>()

const t = useAiI18n()

const icons = computed(() => new Map(props.threads.map((thread) => [thread.id, resolveThreadIcon(props.threadIcon, thread)])))
const iconOf = (thread: RailThreadItem): ThreadIcon | null => icons.value.get(thread.id) ?? null
</script>

<template>
  <div class="t-rail-thread-list">
    <ul v-if="threads.length > 0" class="t-rail-thread-list__rows">
      <li v-for="thread in threads" :key="thread.id">
        <button
          type="button"
          class="t-rail-thread-list__row"
          :class="{ 'is-active': thread.id === (openingThreadId || activeThreadId) }"
          :aria-current="thread.id === (openingThreadId || activeThreadId) ? 'true' : undefined"
          :title="iconOf(thread)?.label || undefined"
          @click="emit('select', thread.id)"
        >
          <!-- Read before the title, as in the sidebar: "<origin> <title>". -->
          <span v-if="iconOf(thread)?.label" class="sr-only">{{ iconOf(thread)!.label }}</span>
          <span class="t-rail-thread-list__title">{{ thread.title }}</span>
          <span v-if="thread.awaitingOwner || thread.running || thread.unread" class="sr-only">{{ thread.awaitingOwner ? t.recents.threadAwaitingOwner : thread.running ? t.recents.threadRunning : t.recents.threadUnread }}</span>
          <span v-if="thread.awaitingOwner || thread.running || thread.unread" class="t-rail-thread-list__icon t-rail-thread-list__status">
            <TThreadStatus :awaiting="thread.awaitingOwner" :running="thread.running" :unread="thread.unread" />
          </span>
          <TThreadIcon v-else-if="iconOf(thread)" class="t-rail-thread-list__icon" :spec="iconOf(thread)!" decorative />
        </button>
      </li>
    </ul>
    <p v-else class="t-rail-thread-list__empty">{{ emptyLabel ?? t.recents.emptyActive }}</p>
    <button
      v-if="showViewAll"
      type="button"
      class="t-rail-thread-list__row t-rail-thread-list__view-all"
      @click="emit('view-all')"
    >
      <span class="t-rail-thread-list__title">{{ viewAllLabel ?? t.recents.viewAll }}</span>
    </button>
  </div>
</template>

<style scoped>
.t-rail-thread-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-rail-thread-list__rows {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.t-rail-thread-list__row {
  display: flex;
  align-items: center;
  width: 100%;
  height: 34px;
  padding: 0 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}
.t-rail-thread-list__row:hover {
  background: var(--tnzi-ai-sidebar-hover);
}
/* The open conversation takes the selected fill, not the hover one the
   expanded list uses: in a panel the pointer is over a row whenever it is
   open, and with one fill for both the open conversation and the hovered one
   could not be told apart. */
.t-rail-thread-list__row.is-active {
  background: var(--tnzi-ai-sidebar-selected);
}
.t-rail-thread-list__row:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
/* At the row's end, clear of the title. */
.t-rail-thread-list__icon {
  margin-left: auto;
  padding-left: 8px;
  box-sizing: content-box;
}
/* The state takes the origin icon's box, so the title's room does not change. */
.t-rail-thread-list__status {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}
.t-rail-thread-list__title {
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.t-rail-thread-list__empty {
  margin: 0;
  padding: 8px 10px 10px;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-rail-thread-list__view-all {
  margin-top: 2px;
  color: var(--tnzi-ai-text-secondary);
  border-top: 1px solid var(--tnzi-ai-divider);
  border-radius: 0 0 8px 8px;
}
</style>
