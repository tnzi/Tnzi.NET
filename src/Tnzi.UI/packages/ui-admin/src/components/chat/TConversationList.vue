<template>
  <div class="t-conv-list">
    <div class="t-conv-list__top">
      <TPresencePicker
        v-if="myStatus != null"
        :status="myStatus"
        :name="myName"
        :avatar-file-id="myAvatarFileId"
        @change="(s) => emit('set-status', s)"
      />
      <div class="t-conv-list__search" :class="{ 't-conv-list__search--focused': focused }">
        <Icon icon="mdi:magnify" :width="16" class="t-conv-list__search-icon" />
        <input
          v-model="keyword"
          class="t-conv-list__search-input"
          :placeholder="t('window.search')"
          @focus="focused = true"
          @blur="focused = false"
        />
        <button
          v-if="keyword"
          class="t-conv-list__search-clear"
          tabindex="-1"
          @click="keyword = ''"
        >
          <Icon icon="mdi:close-circle" :width="14" />
        </button>
      </div>
      <button class="t-conv-list__add" :title="t('window.newChat')" @click="emit('new-chat')">
        <Icon icon="mdi:plus" :width="18" />
      </button>
    </div>
    <NScrollbar class="t-conv-list__scroll">
      <TConversationItem
        v-for="c in ordered"
        :key="c.id"
        :item="c"
        :active="c.id === activeId"
        @select="emit('select', c.id)"
      />
      <div v-if="ordered.length === 0" class="t-conv-list__empty">{{ t('window.empty') }}</div>
    </NScrollbar>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { NScrollbar } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'
import { ConversationType, UserPresenceStatus } from '@tnzi/core/services/chat'
import TConversationItem from './TConversationItem.vue'
import TPresencePicker from './TPresencePicker.vue'
import { translatePageKey } from '../../pages/_shared/translate'

const props = defineProps<{
  conversations: ConversationListItemDto[]
  activeId: string | null
  myStatus?: UserPresenceStatus
  myName?: string
  myAvatarFileId?: string
}>()

const emit = defineEmits<{
  select: [id: string]
  'new-chat': []
  'set-status': [UserPresenceStatus]
}>()

const t = (k: string) => translatePageKey('chat', k)

const keyword = ref('')
const focused = ref(false)
defineExpose({ keyword })

const ordered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  const filtered = kw
    ? props.conversations.filter((c) => (c.title ?? '').toLowerCase().includes(kw))
    : props.conversations

  return [...filtered].sort((a, b) => {
    if (a.type === ConversationType.System && b.type !== ConversationType.System) return -1
    if (b.type === ConversationType.System && a.type !== ConversationType.System) return 1
    if (a.isSticky !== b.isSticky) return a.isSticky ? -1 : 1
    return new Date(b.lastMessageAt ?? 0).getTime() - new Date(a.lastMessageAt ?? 0).getTime()
  })
})
</script>

<style scoped>
.t-conv-list {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--chat-list-bg, #fafafa);
  border-right: 1px solid var(--chat-border, #eaeaea);
}

.t-conv-list__top {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 12px 10px;
  flex-shrink: 0;
}

/* ── Search (custom, neutral focus — no themed ring) ─────────────────────── */
.t-conv-list__search {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 5px;
  height: 30px;
  padding: 0 8px;
  border-radius: 6px;
  background: var(--chat-search-bg, #e9e9e9);
  border: 1px solid transparent;
  transition: background 0.12s, border-color 0.12s;
}

.t-conv-list__search--focused {
  background: var(--chat-surface, #fff);
  border-color: var(--chat-border, #dcdcdc);
}

.t-conv-list__search-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}

.t-conv-list__search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  font-family: inherit;
}

.t-conv-list__search-input::placeholder {
  color: var(--chat-text-3, #a8a8a8);
}

.t-conv-list__search-clear {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-list__search-clear:hover {
  color: var(--chat-text-2, #8a8a8a);
}

/* ── Add button ─────────────────────────────────────────────────────────── */
.t-conv-list__add {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border: none;
  border-radius: 6px;
  background: var(--chat-search-bg, #e9e9e9);
  cursor: pointer;
  color: var(--chat-text-2, #5a5a5a);
  transition: background 0.12s, color 0.12s;
}

.t-conv-list__add:hover {
  background: var(--chat-hover, #e0e0e0);
  color: var(--chat-text, #1f1f1f);
}

.t-conv-list__scroll {
  flex: 1;
  min-height: 0;
}

.t-conv-list__empty {
  padding: 40px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--chat-text-3, #b0b0b0);
}
</style>
