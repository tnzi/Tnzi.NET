<template>
  <NDropdown
    trigger="click"
    :options="options"
    :render-option="renderOption"
    @select="onSelect"
  >
    <div class="t-presence-picker">
      <TChatAvatar :name="name" :file-id="avatarFileId" :status="status" :size="32" />
      <span class="t-presence-picker__name">{{ name || '—' }}</span>
      <Icon icon="mdi:chevron-down" :width="14" class="t-presence-picker__caret" />
    </div>
  </NDropdown>
</template>

<script setup lang="ts">
import { h, computed } from 'vue'
import { NDropdown } from 'naive-ui'
import type { DropdownOption } from 'naive-ui'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import { Icon } from '@iconify/vue'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatAvatar from './TChatAvatar.vue'
import TPresenceDot from './TPresenceDot.vue'

defineProps<{
  status: UserPresenceStatus
  name?: string
  avatarFileId?: string
}>()

const emit = defineEmits<{ change: [status: UserPresenceStatus] }>()

const t = (k: string) => translatePageKey('chat', k)

const STATUS_OPTIONS: { key: UserPresenceStatus; labelKey: string }[] = [
  { key: UserPresenceStatus.Online, labelKey: 'presence.online' },
  { key: UserPresenceStatus.Away, labelKey: 'presence.away' },
  { key: UserPresenceStatus.Busy, labelKey: 'presence.busy' },
  { key: UserPresenceStatus.Invisible, labelKey: 'presence.invisible' },
]

const options = computed<DropdownOption[]>(() =>
  STATUS_OPTIONS.map(({ key, labelKey }) => ({ key, label: t(labelKey) }))
)

function renderOption({ option }: { option: DropdownOption }) {
  return h('div', { class: 't-presence-picker__opt' }, [
    h(TPresenceDot, { status: option.key as UserPresenceStatus, size: 10 }),
    h('span', { class: 't-presence-picker__opt-label' }, option.label as string),
  ])
}

function onSelect(key: string | number) {
  emit('change', key as UserPresenceStatus)
}
</script>

<style scoped>
.t-presence-picker {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  cursor: pointer;
  border-bottom: 1px solid var(--chat-border, #eaeaea);
  background: var(--chat-list-bg, #fafafa);
  transition: background 0.12s;
  flex-shrink: 0;
}

.t-presence-picker:hover {
  background: var(--chat-hover, #e0e0e0);
}

.t-presence-picker__name {
  flex: 1;
  min-width: 0;
  font-size: 13px;
  font-weight: 500;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-presence-picker__caret {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}
</style>

<style>
/* Unscoped — styles the dropdown option content rendered via h() outside the component root */
.t-presence-picker__opt {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 4px;
}

.t-presence-picker__opt-label {
  font-size: 13px;
}
</style>
