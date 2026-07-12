<template>
  <!-- System-conversation notification: a left-aligned card. Distinct from the
       centered grey pill below — in a System feed every item is a notice, so
       centered pills would blend with the timestamp separators and read as
       trivial. A card makes each broadcast read as an actual notification. -->
  <div v-if="message.contentType === MessageContentType.System && isSystem" class="t-bubble-notice">
    <span class="t-bubble-notice__icon"><Icon icon="mdi:bell-outline" :width="15" /></span>
    <div class="t-bubble-notice__card">
      <div v-if="message.category" class="t-bubble-notice__category">{{ message.category }}</div>
      <div v-if="message.title" class="t-bubble-notice__title">{{ message.title }}</div>
      <div class="t-bubble-notice__body">{{ message.content }}</div>
      <a
        v-if="message.linkUrl"
        class="t-bubble-notice__link"
        :href="message.linkUrl"
        target="_blank"
        rel="noopener noreferrer"
      >
        <Icon icon="mdi:open-in-new" :width="13" />
        <span>{{ linkLabel }}</span>
      </a>
    </div>
  </div>

  <!-- Inline system notice (group created / member joined): centered grey pill -->
  <div v-else-if="message.contentType === MessageContentType.System" class="t-bubble-system">
    <span class="t-bubble-system__pill">{{ message.content }}</span>
  </div>

  <!-- Regular message row -->
  <div v-else class="t-bubble-row" :class="{ 't-bubble-row--mine': mine }">
    <TChatAvatar :name="avatarName" :file-id="mine ? myAvatarFileId : message.senderAvatarFileId" :seed="message.senderId" :size="36" class="t-bubble-avatar" />

    <div class="t-bubble-col">
      <!-- Sender name above bubble in group chats -->
      <span v-if="showSender && !mine" class="t-bubble-sender">{{ message.senderName }}</span>

      <!-- Image: NImage gives a click-to-zoom lightbox (with prev/next across the
           whole thread via the NImageGroup wrapper in TMessageList) instead of
           opening a raw link in a new tab. -->
      <NImage
        v-if="message.contentType === MessageContentType.Image"
        class="t-bubble-image"
        :src="fileUrl ?? ''"
        :img-props="{ alt: message.fileName ?? 'image', class: 't-bubble-image__img' }"
        object-fit="cover"
      />

      <!-- File: download chip inside a bubble -->
      <div
        v-else-if="message.contentType === MessageContentType.File"
        class="t-bubble"
        :class="mine ? 't-bubble--mine' : 't-bubble--other'"
      >
        <a class="t-bubble-file" :href="fileUrl ?? '#'" target="_blank" rel="noopener noreferrer" download>
          <span class="t-bubble-file__icon"><Icon :icon="fileIcon" :width="28" :style="{ color: fileIconColor }" /></span>
          <span class="t-bubble-file__meta">
            <span class="t-bubble-file__name">{{ message.fileName ?? 'File' }}</span>
            <span v-if="fileSizeLabel" class="t-bubble-file__size">{{ fileSizeLabel }}</span>
          </span>
        </a>
      </div>

      <!-- Text -->
      <div v-else class="t-bubble" :class="mine ? 't-bubble--mine' : 't-bubble--other'">
        {{ message.content }}
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { NImage } from 'naive-ui'
import { MessageContentType } from '@tnzi/core/services/chat'
import type { ChatMessageDto } from '@tnzi/core/services/chat'
import { resolveChatAvatarUrl } from './avatar'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatAvatar from './TChatAvatar.vue'

const props = defineProps<{
  message: ChatMessageDto
  mine: boolean
  showSender: boolean
  /** True when rendered inside a System (notifications) conversation — system
   *  messages then render as left-aligned notification cards instead of the
   *  inline centered pill used for group notices. */
  isSystem?: boolean
  /** Current user's display name — used for the avatar initial on own messages
   *  (locally-appended messages have no senderName until the server round-trips). */
  myName?: string
  /** Current user's avatar file id — used for the picture on own messages
   *  (own optimistic messages carry no senderAvatarFileId until round-trip). */
  myAvatarFileId?: string | null
}>()

// On my own messages prefer the known current-user name; fall back to senderName.
const avatarName = computed(() => (props.mine ? props.myName || props.message.senderName : props.message.senderName))

// Call-to-action label for a rich notification's link.
const linkLabel = computed(() => translatePageKey('chat', 'window.viewDetails'))

// File/image URL — reuse the file preview helper (/api/files/{id}/preview).
const fileUrl = computed(() => resolveChatAvatarUrl(props.message.fileId))

// File-type icon: map the extension to a recognisable coloured icon so a PDF /
// Word / Excel / archive / media file reads at a glance instead of a generic
// blank document. Falls back to a neutral document icon for unknown types.
const FILE_ICON_MAP: Record<string, { icon: string; color: string }> = {
  pdf: { icon: 'mdi:file-pdf-box', color: '#e2483d' },
  doc: { icon: 'mdi:file-word-box', color: '#2b7cd3' },
  docx: { icon: 'mdi:file-word-box', color: '#2b7cd3' },
  rtf: { icon: 'mdi:file-word-box', color: '#2b7cd3' },
  xls: { icon: 'mdi:file-excel-box', color: '#1f7244' },
  xlsx: { icon: 'mdi:file-excel-box', color: '#1f7244' },
  csv: { icon: 'mdi:file-delimited-outline', color: '#1f7244' },
  ppt: { icon: 'mdi:file-powerpoint-box', color: '#d24726' },
  pptx: { icon: 'mdi:file-powerpoint-box', color: '#d24726' },
  zip: { icon: 'mdi:folder-zip', color: '#f0a020' },
  rar: { icon: 'mdi:folder-zip', color: '#f0a020' },
  '7z': { icon: 'mdi:folder-zip', color: '#f0a020' },
  gz: { icon: 'mdi:folder-zip', color: '#f0a020' },
  tar: { icon: 'mdi:folder-zip', color: '#f0a020' },
  txt: { icon: 'mdi:file-document-outline', color: '#6f6f6f' },
  md: { icon: 'mdi:language-markdown-outline', color: '#6f6f6f' },
  json: { icon: 'mdi:code-json', color: '#8a6d3b' },
  xml: { icon: 'mdi:file-code-outline', color: '#8a6d3b' },
  html: { icon: 'mdi:language-html5', color: '#e44d26' },
  mp3: { icon: 'mdi:file-music-outline', color: '#8e44ad' },
  wav: { icon: 'mdi:file-music-outline', color: '#8e44ad' },
  flac: { icon: 'mdi:file-music-outline', color: '#8e44ad' },
  mp4: { icon: 'mdi:file-video-outline', color: '#2980b9' },
  mov: { icon: 'mdi:file-video-outline', color: '#2980b9' },
  avi: { icon: 'mdi:file-video-outline', color: '#2980b9' },
  mkv: { icon: 'mdi:file-video-outline', color: '#2980b9' },
  png: { icon: 'mdi:file-image-outline', color: '#16a085' },
  jpg: { icon: 'mdi:file-image-outline', color: '#16a085' },
  jpeg: { icon: 'mdi:file-image-outline', color: '#16a085' },
  gif: { icon: 'mdi:file-image-outline', color: '#16a085' },
  webp: { icon: 'mdi:file-image-outline', color: '#16a085' },
}

const fileExt = computed(() => {
  const name = props.message.fileName ?? ''
  const dot = name.lastIndexOf('.')
  return dot >= 0 ? name.slice(dot + 1).toLowerCase() : ''
})
const fileIcon = computed(() => FILE_ICON_MAP[fileExt.value]?.icon ?? 'mdi:file-outline')
const fileIconColor = computed(() => FILE_ICON_MAP[fileExt.value]?.color ?? 'var(--chat-text-2, #6f6f6f)')

const fileSizeLabel = computed(() => {
  const bytes = props.message.fileSize
  if (!bytes || bytes <= 0) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
})
</script>

<style scoped>
/* ── System-conversation notification card (left-aligned) ───────────────── */
.t-bubble-notice {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 2px 16px;
}

.t-bubble-notice__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 26px;
  height: 26px;
  margin-top: 1px;
  border-radius: 50%;
  background: rgb(var(--tnzi-primary-rgb, 13 148 136) / 0.12);
  color: var(--chat-send, var(--tnzi-primary-600, #158278));
}

.t-bubble-notice__card {
  max-width: 78%;
  background: var(--chat-surface, #fff);
  color: var(--chat-text, #1f1f1f);
  border-radius: 6px;
  padding: 9px 13px;
  font-size: 13.5px;
  line-height: 1.6;
  word-break: break-word;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

.t-bubble-notice__category {
  display: inline-block;
  margin-bottom: 4px;
  padding: 1px 8px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  background: rgb(var(--tnzi-primary-rgb, 13 148 136) / 0.12);
  color: var(--chat-send, var(--tnzi-primary-600, #158278));
}

.t-bubble-notice__title {
  font-size: 14px;
  font-weight: 600;
  line-height: 1.4;
  margin-bottom: 2px;
}

.t-bubble-notice__body {
  white-space: pre-wrap;
}

.t-bubble-notice__link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-top: 6px;
  font-size: 12.5px;
  font-weight: 500;
  color: var(--chat-send, var(--tnzi-primary-600, #158278));
  text-decoration: none;
}

.t-bubble-notice__link:hover {
  text-decoration: underline;
}

/* ── System notice ─────────────────────────────────────────────────────── */
.t-bubble-system {
  display: flex;
  justify-content: center;
  margin: 10px 0;
  padding: 0 16px;
}

.t-bubble-system__pill {
  max-width: 80%;
  font-size: 12px;
  line-height: 1.5;
  color: var(--chat-text-3, #9b9b9b);
  background: var(--chat-system-bg, rgba(0, 0, 0, 0.04));
  border-radius: 6px;
  padding: 3px 10px;
  text-align: center;
}

/* ── Message row (avatar + bubble) ──────────────────────────────────────── */
.t-bubble-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 3px 16px;
}

.t-bubble-row--mine {
  flex-direction: row-reverse;
}

.t-bubble-avatar {
  margin-top: 1px;
}

/* ── Column: sender name + bubble ───────────────────────────────────────── */
.t-bubble-col {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-width: 64%;
  min-width: 0;
}

.t-bubble-row--mine .t-bubble-col {
  align-items: flex-end;
}

.t-bubble-sender {
  font-size: 11.5px;
  color: var(--chat-text-3, #9b9b9b);
  padding: 0 2px;
}

/* ── Bubble ─────────────────────────────────────────────────────────────── */
.t-bubble {
  position: relative;
  padding: 9px 13px;
  border-radius: 6px;
  font-size: 14px;
  line-height: 1.6;
  word-break: break-word;
  white-space: pre-wrap;
  max-width: 100%;
}

.t-bubble--mine {
  background: var(--chat-green, #95ec69);
  color: var(--chat-green-text, #0d0d0d);
}

.t-bubble--other {
  background: var(--chat-surface, #fff);
  color: var(--chat-text, #1f1f1f);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

/* Little tail toward the avatar (WeChat-style). */
.t-bubble--mine::before,
.t-bubble--other::before {
  content: '';
  position: absolute;
  top: 12px;
  width: 8px;
  height: 8px;
  transform: rotate(45deg);
}

.t-bubble--mine::before {
  right: -3px;
  background: var(--chat-green, #95ec69);
}

.t-bubble--other::before {
  left: -3px;
  background: var(--chat-surface, #fff);
  box-shadow: -1px 1px 2px rgba(0, 0, 0, 0.04);
}

/* ── Image (NImage — click to open the zoom/prev-next lightbox) ─────────── */
.t-bubble-image {
  display: inline-block;
  line-height: 0;
  border-radius: 6px;
  overflow: hidden;
  border: 1px solid var(--chat-border, #eaeaea);
  max-width: 240px;
  cursor: zoom-in;
}

.t-bubble-image :deep(.t-bubble-image__img),
.t-bubble-image :deep(img) {
  display: block;
  max-width: 240px;
  max-height: 240px;
  width: auto;
  height: auto;
  object-fit: cover;
}

/* ── File chip ──────────────────────────────────────────────────────────── */
.t-bubble-file {
  display: flex;
  align-items: center;
  gap: 10px;
  color: inherit;
  text-decoration: none;
  min-width: 160px;
  max-width: 240px;
}

.t-bubble-file__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 34px;
  height: 34px;
  border-radius: 5px;
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.08);
  color: var(--chat-text-2, #6f6f6f);
}

.t-bubble-file__meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.t-bubble-file__name {
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-bubble-file__size {
  font-size: 11px;
  color: var(--chat-text-3, #9b9b9b);
}
</style>
