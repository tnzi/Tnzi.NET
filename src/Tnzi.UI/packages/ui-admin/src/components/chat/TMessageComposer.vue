<template>
  <div class="t-composer" :class="{ 't-composer--disabled': disabled }">
    <!-- Upload progress bar (shown while a file/image is uploading) -->
    <div v-if="uploading" class="t-composer__upload">
      <Icon
        :icon="uploadKind === 'image' ? 'mdi:image-outline' : 'mdi:file-upload-outline'"
        :width="16"
        class="t-composer__upload-icon"
      />
      <span class="t-composer__upload-name">{{ uploadName || t('window.uploading') }}</span>
      <span class="t-composer__upload-pct">{{ uploadProgress ?? 0 }}%</span>
      <div class="t-composer__upload-track">
        <div class="t-composer__upload-fill" :style="{ width: `${uploadProgress ?? 0}%` }" />
      </div>
    </div>

    <!-- Textarea on top -->
    <textarea
      ref="textareaRef"
      v-model="text"
      class="t-composer__textarea"
      :disabled="disabled"
      :placeholder="t('window.inputPlaceholder')"
      rows="3"
      @keydown="onKeydown"
    />

    <!-- Bottom bar: tools on the left, Send on the right (WeChat layout) -->
    <div class="t-composer__bar">
      <div class="t-composer__tools">
        <button class="t-composer__tool" :disabled="disabled" :title="t('window.emoji')" @click="insertEmoji">
          <Icon icon="mdi:emoticon-happy-outline" :width="21" />
        </button>
        <button class="t-composer__tool" :disabled="disabled" :title="t('window.image')" @click="emit('pick-file', 'image')">
          <Icon icon="mdi:image-outline" :width="21" />
        </button>
        <button class="t-composer__tool" :disabled="disabled" :title="t('window.file')" @click="emit('pick-file', 'file')">
          <Icon icon="mdi:folder-outline" :width="21" />
        </button>
      </div>
      <button class="t-composer__send" :disabled="disabled || !text.trim()" @click="onSend">
        {{ t('window.send') }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import { translatePageKey } from '../../pages/_shared/translate'

const props = defineProps<{
  disabled?: boolean
  /** Live upload state — shows a progress bar above the textarea while a
   *  file/image is uploading so the user isn't left staring at a frozen UI. */
  uploading?: boolean
  uploadProgress?: number
  uploadKind?: 'image' | 'file'
  uploadName?: string
}>()

const emit = defineEmits<{
  send: [text: string]
  'pick-file': [type: 'image' | 'file']
}>()

const t = (k: string) => translatePageKey('chat', k)

const text = ref('')
const textareaRef = ref<HTMLTextAreaElement | null>(null)

function onSend() {
  if (props.disabled) return
  const trimmed = text.value.trim()
  if (!trimmed) return
  emit('send', trimmed)
  text.value = ''
}

function onKeydown(e: KeyboardEvent) {
  // Enter without Shift → send; Shift+Enter → native newline
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    onSend()
  }
}

/** Insert a simple emoji at the cursor position. */
function insertEmoji() {
  if (props.disabled) return
  const el = textareaRef.value
  if (!el) {
    text.value += '😊'
    return
  }
  const start = el.selectionStart ?? text.value.length
  const end = el.selectionEnd ?? text.value.length
  text.value = text.value.slice(0, start) + '😊' + text.value.slice(end)
  const pos = start + '😊'.length
  el.focus()
  el.setSelectionRange(pos, pos)
}
</script>

<style scoped>
.t-composer {
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--chat-border, #e6e6e6);
  background: var(--chat-surface, #fff);
}

.t-composer--disabled {
  opacity: 0.6;
  pointer-events: none;
}

/* ── Upload progress ────────────────────────────────────────────────────── */
.t-composer__upload {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 14px 0;
  font-size: 12px;
  color: var(--chat-text-2, #6f6f6f);
}

.t-composer__upload-icon {
  flex-shrink: 0;
  color: var(--chat-send, #158278);
}

.t-composer__upload-name {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-composer__upload-pct {
  flex-shrink: 0;
  font-variant-numeric: tabular-nums;
  color: var(--chat-send, #158278);
}

.t-composer__upload-track {
  flex: 1;
  min-width: 0;
  height: 4px;
  border-radius: 2px;
  background: var(--chat-hover, #efefef);
  overflow: hidden;
}

.t-composer__upload-fill {
  height: 100%;
  border-radius: 2px;
  background: var(--chat-send, #158278);
  transition: width 0.15s ease;
}

/* ── Textarea (top) ─────────────────────────────────────────────────────── */
.t-composer__textarea {
  flex: 1;
  min-width: 0;
  resize: none;
  border: none;
  outline: none;
  font-size: 14px;
  line-height: 1.6;
  color: var(--chat-text, #1f1f1f);
  background: transparent;
  font-family: inherit;
  padding: 12px 16px 6px;
}

.t-composer__textarea::placeholder {
  color: var(--chat-text-3, #b5b5b5);
}

/* ── Bottom bar: tools (left) + Send (right) ────────────────────────────── */
.t-composer__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 12px 10px;
}

.t-composer__tools {
  display: flex;
  align-items: center;
  gap: 2px;
}

.t-composer__tool {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  color: var(--chat-text-2, #6f6f6f);
  transition: background 0.12s, color 0.12s;
}

.t-composer__tool:hover:not(:disabled) {
  background: var(--chat-hover, #efefef);
  color: var(--chat-text, #1f1f1f);
}

.t-composer__tool:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.t-composer__send {
  flex-shrink: 0;
  height: 28px;
  padding: 0 20px;
  border: none;
  border-radius: 4px;
  background: var(--chat-send, #158278);
  color: #fff;
  font-size: 13px;
  font-weight: 500;
  line-height: 1;
  cursor: pointer;
  transition: background 0.12s, opacity 0.12s;
}

.t-composer__send:hover:not(:disabled) {
  background: var(--chat-send-hover, #06ad56);
}

.t-composer__send:disabled {
  background: var(--chat-send-disabled, #c4e8d4);
  color: #fff;
  cursor: not-allowed;
}
</style>
