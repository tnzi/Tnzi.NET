<script setup lang="ts">
/**
 * TSlashCommandMenu - the composer's `/` menu as it looks: the commands that match what is typed
 * (`useSlashMenu().results`), the highlighted one, and a line saying why the list is empty. It is
 * a listbox the composer's box controls (`aria-activedescendant` on the box, `<id>-<index>` on each
 * option): focus stays in the box while the user types and moves through the list, and a click
 * picks without taking focus away from it. `TChatApp` draws it in place of `#composer-suggestions`
 * when it has `slashCommands`; on its own, render it in `TThreadComposer`'s `#suggestions` slot.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n } from '../../i18n/index'
import type { SlashMenuItem } from '../../headless/slash-menu'

/** Where the list of commands stands: still being read, read, or the read failed. */
export type SlashCommandsState = 'idle' | 'loading' | 'ready' | 'failed'

const props = withDefaults(
  defineProps<{
    /** The matching commands, best first. */
    items: readonly SlashMenuItem[]
    highlighted: number
    /** The listbox id; each option is `<id>-<index>`. */
    id?: string
    state?: SlashCommandsState
    /** A line under the list (why some commands are missing, say). */
    note?: string | null
  }>(),
  { id: 'tnzi-slash-menu', state: 'ready', note: null },
)

const emit = defineEmits<{ pick: [index: number]; highlight: [index: number] }>()

const t = useAiI18n()

/** The full command when the menu shows a shorter name, so the user sees what is written. */
function fullCommand(item: SlashMenuItem): string | null {
  return item.command === item.name ? null : `/${item.command}`
}

const status = computed(() => {
  if (props.items.length > 0) return ''
  if (props.state === 'loading' || props.state === 'idle') return t.value.slash.loading
  if (props.state === 'failed') return t.value.slash.loadFailed
  return t.value.slash.none
})

const list = ref<HTMLElement | null>(null)
watch(
  () => props.highlighted,
  (index) => {
    void nextTick(() => list.value?.querySelector<HTMLElement>(`[data-index="${index}"]`)?.scrollIntoView?.({ block: 'nearest' }))
  },
)
</script>

<template>
  <div class="t-slash-menu" data-slash-menu>
    <div class="t-slash-menu__head">{{ t.slash.title }}</div>
    <div v-if="items.length > 0" :id="id" ref="list" class="t-slash-menu__list t-ai-scroll" role="listbox" :aria-label="t.slash.title">
      <div
        v-for="(item, index) in items"
        :id="`${id}-${index}`"
        :key="item.command"
        role="option"
        class="t-slash-menu__item"
        :class="{ 'is-active': index === highlighted }"
        :aria-selected="index === highlighted"
        :data-index="index"
        :data-command="item.command"
        @mousedown.prevent
        @mousemove="index !== highlighted && emit('highlight', index)"
        @click="emit('pick', index)"
      >
        <Icon :icon="item.icon || 'lucide:terminal'" class="t-slash-menu__icon" aria-hidden="true" />
        <span class="t-slash-menu__body">
          <span class="t-slash-menu__line">
            <span class="t-slash-menu__name">/{{ item.name }}</span>
            <span v-if="item.argumentHint" class="t-slash-menu__hint">{{ item.argumentHint }}</span>
            <span v-if="item.sourceLabel" class="t-slash-menu__source">{{ item.sourceLabel }}</span>
          </span>
          <span v-if="item.description" class="t-slash-menu__desc">{{ item.description }}</span>
          <span v-if="fullCommand(item)" class="t-slash-menu__command">{{ fullCommand(item) }}</span>
        </span>
      </div>
    </div>
    <p v-else class="t-slash-menu__status" role="status">{{ status }}</p>
    <p v-if="note" class="t-slash-menu__note">{{ note }}</p>
    <p class="t-slash-menu__keys" aria-hidden="true">{{ t.slash.keys }}</p>
  </div>
</template>

<style scoped>
/* The popover's surface (TPopoverMenu's look), filling the place above the composer. */
.t-slash-menu {
  box-sizing: border-box;
  max-height: min(420px, 60vh);
  display: flex;
  flex-direction: column;
  padding: 6px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
  box-shadow: var(--tnzi-ai-popover-shadow);
  color: var(--tnzi-ai-text);
  text-align: left;
}
.t-slash-menu__head {
  padding: 6px 10px 4px;
  font-size: 12px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
}
.t-slash-menu__list {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 1px;
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}
.t-slash-menu__item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  box-sizing: border-box;
  width: 100%;
  padding: 7px 10px;
  border-radius: 8px;
  color: var(--tnzi-ai-text);
  cursor: pointer;
}
.t-slash-menu__item.is-active {
  background: var(--tnzi-ai-hover);
}
.t-slash-menu__icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 15px;
  color: var(--tnzi-ai-text-secondary);
}
.t-slash-menu__body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.t-slash-menu__line {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}
.t-slash-menu__name {
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-slash-menu__hint {
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
  white-space: nowrap;
}
.t-slash-menu__source {
  margin-left: auto;
  flex-shrink: 0;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
  white-space: nowrap;
}
.t-slash-menu__desc {
  font-size: 12px;
  line-height: 1.45;
  color: var(--tnzi-ai-text-secondary);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.t-slash-menu__command {
  font-family: var(--tnzi-ai-font-mono);
  font-size: 11px;
  color: var(--tnzi-ai-text-tertiary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-slash-menu__status,
.t-slash-menu__note {
  margin: 4px 10px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-tertiary);
}
.t-slash-menu__keys {
  margin: 4px 10px 2px;
  padding-top: 6px;
  border-top: 1px solid var(--tnzi-ai-divider);
  font-size: 11px;
  color: var(--tnzi-ai-text-tertiary);
}
@media (max-width: 480px) {
  .t-slash-menu__keys {
    display: none;
  }
}
</style>
