<script setup lang="ts">
/**
 * @experimental
 * TThreadList - conversation history section for a chat sidebar.
 *
 * A titled section, a row per thread, and a hover delete affordance that
 * confirms **inline** rather than through a modal: a thread list is a place
 * people delete things quickly, and a dialog per row would be heavier than the
 * action it guards.
 *
 * The confirmation is a single-value state machine owned here (`confirmingId`),
 * so at most one row can be mid-confirm and switching rows cancels the previous
 * one for free. The consumer only hears about the decision, via `delete`.
 *
 * Row geometry deliberately matches `TSidebarNav`'s items (h-36, p-9, gap-12,
 * radius-10, icon-18) so every line in the expanded sidebar aligns.
 */
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n } from '../../i18n/index'

export interface ThreadItem {
  readonly id: string
  readonly title: string
  readonly updatedAt?: string
}

withDefaults(
  defineProps<{
    threads?: ReadonlyArray<ThreadItem>
    activeThreadId?: string
    /** Section heading. */
    label?: string
    /** Hover delete affordance with inline confirm. */
    enableDelete?: boolean
    /** Inline confirm prompt. */
    confirmLabel?: string
    /** Show the `+` button in the section header. */
    showAddButton?: boolean
  }>(),
  {
    threads: () => [],
    activeThreadId: undefined,
    label: 'All tasks',
    enableDelete: true,
    confirmLabel: 'Delete?',
    showAddButton: true,
  },
)

const emit = defineEmits<{
  select: [threadId: string]
  delete: [threadId: string]
  add: []
}>()

const t = useAiI18n()

const confirmingId = ref<string | null>(null)

function ask(id: string): void {
  confirmingId.value = id
}
function confirm(id: string): void {
  emit('delete', id)
  confirmingId.value = null
}
function cancel(): void {
  confirmingId.value = null
}
</script>

<template>
  <div class="t-thread-list">
    <div v-if="label || showAddButton" class="t-thread-list__head">
      <span>{{ label }}</span>
      <button
        v-if="showAddButton"
        type="button"
        class="t-thread-list__head-act"
        :aria-label="t.chat.newChat"
        @click="emit('add')"
      >
        <Icon icon="lucide:plus" />
      </button>
    </div>

    <div
      v-for="thread in threads"
      :key="thread.id"
      class="t-thread-list__row"
      :class="{ 'is-active': thread.id === activeThreadId }"
    >
      <template v-if="confirmingId === thread.id">
        <span class="t-thread-list__confirm-label">{{ confirmLabel }}</span>
        <button
          type="button"
          class="t-thread-list__confirm t-thread-list__confirm--yes"
          @click="confirm(thread.id)"
        >{{ t.common.yes }}</button>
        <button
          type="button"
          class="t-thread-list__confirm"
          @click="cancel"
        >{{ t.common.no }}</button>
      </template>
      <template v-else>
        <button
          type="button"
          class="t-thread-list__select"
          @click="emit('select', thread.id)"
        >
          <slot name="row" :thread="thread">
            <Icon icon="lucide:message-square" class="t-thread-list__icon" />
            <span class="t-thread-list__title">{{ thread.title }}</span>
          </slot>
        </button>
        <button
          v-if="enableDelete"
          type="button"
          class="t-thread-list__del"
          :aria-label="t.chat.deleteConversation"
          @click="ask(thread.id)"
        >
          <Icon icon="lucide:x" />
        </button>
      </template>
    </div>

    <slot name="after" />
  </div>
</template>

<style scoped>
.t-thread-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 14px 10px 4px;
}
.t-thread-list__head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 10px 4px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.01em;
}
.t-thread-list__head > span:first-child { flex: 1; }
.t-thread-list__head-act {
  width: 20px;
  height: 20px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
}
.t-thread-list__head-act:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}

.t-thread-list__row {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  height: 36px;
  padding: 0 9px;
  border: none;
  background: transparent;
  border-radius: 10px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  cursor: pointer;
  text-align: left;
  min-width: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-list__row:hover { background: var(--tnzi-ai-hover); }
.t-thread-list__row.is-active {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent);
  font-weight: 500;
  position: relative;
}
.t-thread-list__row.is-active::before {
  content: '';
  position: absolute;
  left: 0;
  top: 50%;
  transform: translateY(-50%);
  width: 3px;
  height: 18px;
  border-radius: 0 3px 3px 0;
  background: var(--tnzi-ai-accent);
}
.t-thread-list__row.is-active .t-thread-list__icon {
  color: var(--tnzi-ai-accent);
}
.t-thread-list__select {
  display: flex;
  align-items: center;
  gap: 12px;
  flex: 1;
  min-width: 0;
  border: none;
  background: none;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  padding: 0;
}
/* Hidden until hover/focus so a long list is not a wall of X buttons, but
   `:focus-visible` keeps it reachable by keyboard. */
.t-thread-list__del {
  flex-shrink: 0;
  width: 22px;
  height: 22px;
  border: none;
  background: none;
  color: var(--tnzi-ai-text-tertiary);
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-thread-list__row:hover .t-thread-list__del,
.t-thread-list__del:focus-visible {
  opacity: 1;
}
.t-thread-list__del:hover {
  background: var(--tnzi-ai-press);
  color: var(--tnzi-ai-danger);
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
.t-thread-list__icon {
  flex-shrink: 0;
  width: 18px;
  height: 18px;
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary);
}
.t-thread-list__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
