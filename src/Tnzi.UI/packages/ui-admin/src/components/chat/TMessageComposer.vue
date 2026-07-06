<template>
  <div
    class="t-composer"
    :class="{ 't-composer--disabled': disabled, 't-composer--dragover': dragging }"
    @dragenter.prevent="onDragEnter"
    @dragover.prevent
    @dragleave.prevent="onDragLeave"
    @drop.prevent="onDrop"
  >
    <!-- Drop hint overlay (shown while a file is dragged over the input area) -->
    <div v-if="dragging" class="t-composer__drop">
      <Icon icon="mdi:tray-arrow-up" :width="22" />
      <span>{{ t('window.dropToSend') }}</span>
    </div>

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
        <!-- Emoji: arrow popover with a frequently-used group + the full grid -->
        <NPopover trigger="click" :show="emojiOpen" :show-arrow="true" :z-index="POPOVER_Z" placement="top-start" raw @update:show="onEmojiToggle">
          <template #trigger>
            <button class="t-composer__tool" :disabled="disabled" :title="t('window.emoji')">
              <Icon icon="mdi:emoticon-happy-outline" :width="21" />
            </button>
          </template>
          <div class="t-composer__emoji-pop">
            <template v-if="frequent.length">
              <div class="t-composer__emoji-title">{{ t('window.recentEmoji') }}</div>
              <div class="t-composer__emoji-grid">
                <button v-for="(e, i) in frequent" :key="`f${i}`" type="button" class="t-composer__emoji" @click="insertEmoji(e)">{{ e }}</button>
              </div>
            </template>
            <div class="t-composer__emoji-title">{{ t('window.allEmoji') }}</div>
            <div class="t-composer__emoji-grid t-composer__emoji-grid--scroll">
              <button v-for="(e, i) in EMOJIS" :key="i" type="button" class="t-composer__emoji" @click="insertEmoji(e)">{{ e }}</button>
            </div>
          </div>
        </NPopover>

        <!-- Single attachment button — one file picker; the message kind (image
             vs file) is derived from the chosen file's MIME type downstream.
             Hidden entirely when the deployment disables file messages. -->
        <button
          v-if="attachments !== false"
          class="t-composer__tool"
          :disabled="disabled"
          :title="t('window.attachment')"
          @click="emit('pick-file')"
        >
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
import { NPopover } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { translatePageKey } from '../../pages/_shared/translate'
import { COMMON_EMOJIS, getFrequentEmojis, recordEmojiUse } from './emoji'

const props = defineProps<{
  disabled?: boolean
  /** Live upload state — shows a progress bar above the textarea while a
   *  file/image is uploading so the user isn't left staring at a frozen UI. */
  uploading?: boolean
  uploadProgress?: number
  uploadKind?: 'image' | 'file'
  uploadName?: string
  /** Deployment file-message toggle — false hides the attachment button and
   *  ignores drag-and-drop (the server rejects media messages regardless). */
  attachments?: boolean
}>()

const emit = defineEmits<{
  send: [text: string]
  'pick-file': []
  'drop-file': [file: File]
}>()

const t = (k: string) => translatePageKey('chat', k)

// Popover must clear the chat NModal — give it a z-index above it.
const POPOVER_Z = 3000
const EMOJIS = COMMON_EMOJIS

const text = ref('')
const textareaRef = ref<HTMLTextAreaElement | null>(null)
const emojiOpen = ref(false)
const frequent = ref<string[]>([])

// ── File drag-and-drop onto the input area ──────────────────────────────────
// A depth counter avoids the flicker of dragenter/dragleave firing as the
// cursor crosses child elements — `dragging` only clears when the drag truly
// leaves the composer (or on drop).
const dragging = ref(false)
let dragDepth = 0
function onDragEnter() {
  if (props.disabled || props.attachments === false) return
  dragDepth++
  dragging.value = true
}
function onDragLeave() {
  dragDepth--
  if (dragDepth <= 0) {
    dragDepth = 0
    dragging.value = false
  }
}
function onDrop(e: DragEvent) {
  dragDepth = 0
  dragging.value = false
  if (props.disabled || props.attachments === false) return
  const file = e.dataTransfer?.files?.[0]
  if (file) emit('drop-file', file)
}

function onEmojiToggle(show: boolean) {
  emojiOpen.value = show
  // Refresh the frequently-used group from storage each time the panel opens.
  if (show) frequent.value = getFrequentEmojis()
}

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

/** Insert the chosen emoji at the cursor position, record its use (for the
 *  frequently-used group) and close the panel. */
function insertEmoji(emoji: string) {
  if (props.disabled) return
  recordEmojiUse(emoji)
  emojiOpen.value = false
  const el = textareaRef.value
  if (!el) {
    text.value += emoji
    return
  }
  const start = el.selectionStart ?? text.value.length
  const end = el.selectionEnd ?? text.value.length
  text.value = text.value.slice(0, start) + emoji + text.value.slice(end)
  const pos = start + emoji.length
  el.focus()
  // Restore the caret right after the inserted glyph on the next tick.
  requestAnimationFrame(() => el.setSelectionRange(pos, pos))
}
</script>

<style scoped>
.t-composer {
  position: relative;
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--chat-border, #e6e6e6);
  background: var(--chat-surface, #fff);
}

/* ── Drag-and-drop hint ─────────────────────────────────────────────────── */
.t-composer--dragover {
  outline: 2px dashed var(--chat-send, #158278);
  outline-offset: -4px;
}

.t-composer__drop {
  position: absolute;
  inset: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  font-size: 13.5px;
  font-weight: 500;
  color: var(--chat-send, #158278);
  background: rgb(var(--tnzi-primary-rgb, 21 130 120) / 0.08);
  pointer-events: none;
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

<style>
/* Unscoped — emoji panel renders inside the teleported `raw` popover (so we own
   all padding; naive's default popover padding caused an oversized right gap). */
.t-composer__emoji-pop {
  width: 300px;
  padding: 8px;
  background: var(--chat-surface, #fff);
  border: 1px solid var(--chat-border, #e6e6e6);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: var(--tnzi-shadow-popover, 0 6px 24px rgba(0, 0, 0, 0.16));
}

.t-composer__emoji-title {
  font-size: 11px;
  color: var(--chat-text-3, #a8a8a8);
  padding: 2px 2px 4px;
}

/* repeat(8, 1fr) makes the columns fill the full width — no leftover right
   margin from fixed-width cells. */
.t-composer__emoji-grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
}

.t-composer__emoji-grid--scroll {
  max-height: 176px;
  overflow-y: auto;
  scrollbar-width: thin;
}

.t-composer__emoji-grid--scroll::-webkit-scrollbar {
  width: 6px;
}

.t-composer__emoji-grid--scroll::-webkit-scrollbar-thumb {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.2);
  border-radius: 3px;
}

.t-composer__emoji {
  border: none;
  background: transparent;
  cursor: pointer;
  font-size: 20px;
  line-height: 1;
  height: 32px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background 0.1s;
}

.t-composer__emoji:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.08));
}
</style>
