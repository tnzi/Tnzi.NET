<template>
  <TChatDialog
    :show="show"
    :title="t('window.searchHistory')"
    :close-label="t('close')"
    width="380px"
    @update:show="emit('update:show', $event)"
  >
    <div class="t-search-history__search" :class="{ 't-search-history__search--focused': focused }">
      <Icon icon="mdi:magnify" :width="15" class="t-search-history__search-icon" />
      <input
        v-model="keyword"
        class="t-search-history__search-input"
        :placeholder="t('window.search')"
        @input="onSearch"
        @focus="focused = true"
        @blur="focused = false"
      />
      <button v-if="keyword" class="t-search-history__search-clear" tabindex="-1" @click="keyword = ''">
        <Icon icon="mdi:close-circle" :width="13" />
      </button>
    </div>

    <!-- Inline height: NScrollbar's root does not inherit the scoped attr. -->
    <NScrollbar class="t-search-history__list" style="height: 320px">
      <div
        v-for="m in results"
        :key="m.id"
        class="t-search-history__item"
        @click="onResultClick(m.id)"
      >
        <div class="t-search-history__meta">
          <span class="t-search-history__sender">{{ m.senderName || '—' }}</span>
          <span class="t-search-history__time">{{ formatDateTime(m.sentAt) }}</span>
        </div>
        <div class="t-search-history__content">{{ m.content }}</div>
      </div>
      <div v-if="results.length === 0" class="t-search-history__empty">
        {{ emptyHint }}
      </div>
    </NScrollbar>
  </TChatDialog>
</template>

<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { Icon } from '@iconify/vue'
import { NScrollbar } from 'naive-ui'
import type { ChatMessageDto } from '@tnzi/core/services/chat'
import { formatDateTime } from '@tnzi/core'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatDialog from './TChatDialog.vue'

const props = defineProps<{
  show: boolean
  conversationId: string | null
}>()

const emit = defineEmits<{
  'update:show': [v: boolean]
  jump: [messageId: string]
}>()

const t = (k: string) => translatePageKey('chat', k)
const store = useChatStore()

const keyword = ref('')
const focused = ref(false)
const results = ref<ChatMessageDto[]>([])
const loading = ref(false)

const emptyHint = computed(() =>
  loading.value
    ? t('window.loading')
    : keyword.value.trim()
      ? t('window.empty')
      : t('window.searchHistoryHint'),
)

watch(() => props.show, (open) => { if (open) { keyword.value = ''; results.value = [] } })

let timer: ReturnType<typeof setTimeout> | null = null
function onSearch() {
  if (timer) clearTimeout(timer)
  timer = setTimeout(async () => {
    if (!props.conversationId || !keyword.value.trim()) { results.value = []; return }
    loading.value = true
    try {
      const thread = await store.searchMessages(props.conversationId, keyword.value.trim())
      results.value = thread.messages
    } finally {
      loading.value = false
    }
  }, 300)
}

function onResultClick(messageId: string) {
  // v1: surface the message id to the parent (scroll-to-message is a follow-up).
  emit('jump', messageId)
}

onUnmounted(() => { if (timer) clearTimeout(timer) })

defineExpose({ keyword, results })
</script>

<style scoped>
.t-search-history__search {
  display: flex;
  align-items: center;
  gap: 5px;
  height: 32px;
  padding: 0 8px;
  border-radius: 6px;
  background: var(--chat-search-bg, #e9e9e9);
  border: 1px solid transparent;
  transition: background 0.12s, border-color 0.12s;
}

.t-search-history__search--focused {
  background: var(--chat-surface, #fff);
  border-color: var(--chat-border, #dcdcdc);
}

.t-search-history__search-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}

.t-search-history__search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  font-family: inherit;
}

.t-search-history__search-input::placeholder {
  color: var(--chat-text-3, #a8a8a8);
}

.t-search-history__search-clear {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
}

.t-search-history__search-clear:hover {
  color: var(--chat-text-2, #8a8a8a);
}

/* Height set inline on NScrollbar (scoped attr not inherited by its root). */
.t-search-history__item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 8px 6px;
  cursor: pointer;
  border-radius: 6px;
  transition: background 0.12s;
}

.t-search-history__item:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
}

.t-search-history__meta {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  font-size: 11px;
  color: var(--chat-text-3, #a8a8a8);
}

.t-search-history__sender {
  font-weight: 600;
  color: var(--chat-text-2, #6f6f6f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-search-history__time {
  flex-shrink: 0;
}

.t-search-history__content {
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-search-history__empty {
  padding: 40px 8px;
  text-align: center;
  font-size: 13px;
  color: var(--chat-text-3, #b0b0b0);
}
</style>
