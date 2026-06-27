<template>
  <NModal
    :show="show"
    :mask-closable="false"
    :close-on-esc="false"
    transform-origin="center"
    @update:show="emit('update:show', $event)"
  >
    <div class="t-chat-window" :class="{ 't-chat-window--max': maximized }" :style="windowStyle">
      <!-- Window title bar: a slim drag strip over the conversation pane (right
           column only) holding the window controls. The conversation list owns
           the full height of the left column, so its avatar/search row sits at
           the very top with no empty strip above it. Keeping maximize/close here
           means the pane's "..." info button reads as a content action. -->
      <div class="t-chat-window__titlebar" @mousedown="onDragStart">
        <div class="t-chat-window__winctl" @mousedown.stop>
          <button
            v-if="!isSm"
            class="t-chat-window__winbtn"
            :title="maximized ? t('window.restore') : t('window.maximize')"
            @click="onToggleMaximize"
          >
            <Icon :icon="maximized ? 'mdi:window-restore' : 'mdi:window-maximize'" :width="15" />
          </button>
          <button
            class="t-chat-window__winbtn t-chat-window__winbtn--close"
            :title="t('close')"
            @click="emit('update:show', false)"
          >
            <Icon icon="mdi:close" :width="16" />
          </button>
        </div>
      </div>

      <!-- On phones the list and pane share one column and swap; the Transitions
           give an iOS-style push (pane slides in from the right over the list,
           the list parallax-shifts left). On desktop both are always shown
           (conditions stable) so the transitions never fire. -->
      <Transition name="t-chat-list">
        <TConversationList
          v-if="!isSm || !showPane"
          class="t-chat-window__left"
          :conversations="store.sortedConversations"
          :active-id="store.activeId"
          :my-status="store.myStatus"
          :my-name="auth.userInfo?.displayName || auth.userInfo?.username"
          :my-avatar-file-id="auth.userInfo?.avatarId ?? undefined"
          @select="onSelect"
          @new-chat="newChatShow = true"
          @set-status="(s) => store.setMyStatus(s)"
        />
      </Transition>
      <Transition name="t-chat-pane">
        <TConversationPane
          v-if="!isSm || showPane"
          class="t-chat-window__right"
          :conversation="store.activeConversation"
          :messages="store.activeId ? (store.messagesByConv[store.activeId] ?? []) : []"
          :my-id="auth.userInfo?.id"
          :my-name="auth.userInfo?.displayName || auth.userInfo?.username"
          :my-avatar-file-id="auth.userInfo?.avatarId ?? undefined"
          :uploading="uploading"
          :upload-progress="uploadProgress"
          :upload-kind="uploadKind"
          :upload-name="uploadName"
          :info-show="infoShow"
          @send="(text) => store.activeId && store.sendText(store.activeId, text)"
          @pick-file="onPickFile"
          @drop-file="onDroppedFile"
          @toggle-info="infoShow = !infoShow"
          @update:info-show="infoShow = $event"
          @panel-changed="() => store.fetchConversations()"
          @open-conversation="onOpenConversation"
          @back="onBack"
        />
      </Transition>
    </div>
  </NModal>

  <!-- New chat dialog -->
  <TNewChatDialog
    :show="newChatShow"
    @update:show="newChatShow = $event"
    @created="onChatCreated"
  />

  <!-- Hidden file input for media uploads (one picker — the message kind is
       derived from the chosen file's MIME type). -->
  <input
    ref="fileInputRef"
    type="file"
    accept="*/*"
    style="display: none"
    @change="onFileSelected"
  />
</template>

<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import { NModal, useMessage } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { MessageContentType } from '@tnzi/core/services/chat'
import { useStorageApi } from '@tnzi/core/services/storage'
import { formatFileSize } from '@tnzi/core'
import { useChatStore } from '../../stores/useChatStore'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { unwrapResult } from '../../services/_mappers'
import { translatePageKey, interpolate } from '../../pages/_shared/translate'
import TConversationList from './TConversationList.vue'
import TConversationPane from './TConversationPane.vue'
import TNewChatDialog from './TNewChatDialog.vue'

const props = defineProps<{ show: boolean }>()
const emit = defineEmits<{ 'update:show': [v: boolean] }>()

const store = useChatStore()
const auth = useAdminAuthStore()
const client = useAdminClient(false)
const { isSm } = useBreakpoint()

const t = (k: string) => translatePageKey('chat', k)

// Naive message API for upload feedback. Resolved defensively so the component
// still mounts in unit tests (which don't wrap it in an <n-message-provider>);
// the admin shell (TAdminAppRoot) always provides one in the real app.
let message: ReturnType<typeof useMessage> | null = null
try {
  message = useMessage()
} catch {
  message = null
}

// Client-side guard mirroring the Storage module default (`StorageOptions.
// MaxFileSize` = 100 MB). The server is authoritative — a smaller deployment
// limit is still caught by the upload-result error below — but this gives
// instant feedback for an obviously-oversized file instead of streaming it all
// the way up only to be rejected.
const MAX_FILE_SIZE = 100 * 1024 * 1024

const showPane = ref(false)
const newChatShow = ref(false)
const infoShow = ref(false)
// Maximize the whole window to near-full-viewport and back to the 840×670 base.
const maximized = ref(false)

// ── Window dragging (by the conversation-pane title bar) ────────────────────
// The window is centered by NModal; a translate offset moves it from there.
// Disabled while maximized (it already fills the viewport).
const dragOffset = ref({ x: 0, y: 0 })
const windowStyle = computed(() =>
  maximized.value ? {} : { transform: `translate(${dragOffset.value.x}px, ${dragOffset.value.y}px)` },
)

let dragStart = { x: 0, y: 0, offX: 0, offY: 0 }
function onDragMove(e: MouseEvent) {
  dragOffset.value = { x: dragStart.offX + (e.clientX - dragStart.x), y: dragStart.offY + (e.clientY - dragStart.y) }
}
function onDragEnd() {
  window.removeEventListener('mousemove', onDragMove)
  window.removeEventListener('mouseup', onDragEnd)
  document.body.style.userSelect = ''
}
function onDragStart(e: MouseEvent) {
  if (maximized.value) return
  dragStart = { x: e.clientX, y: e.clientY, offX: dragOffset.value.x, offY: dragOffset.value.y }
  document.body.style.userSelect = 'none'
  window.addEventListener('mousemove', onDragMove)
  window.addEventListener('mouseup', onDragEnd)
}

function onToggleMaximize() {
  maximized.value = !maximized.value
  // Recenter when toggling so the offset from a previous drag doesn't linger.
  dragOffset.value = { x: 0, y: 0 }
}

// Closing the window resets transient view state so reopening is always a clean
// centered, base-size window — and, crucially, the info panel never reopens in a
// stale blank state (its content only loads on a show→true / id-change watch).
watch(
  () => props.show,
  (open) => {
    if (!open) {
      infoShow.value = false
      maximized.value = false
      dragOffset.value = { x: 0, y: 0 }
    }
  },
)

onUnmounted(onDragEnd)

function onSelect(id: string) {
  // Switching conversations closes the info panel so it never shows stale detail.
  infoShow.value = false
  void store.openConversation(id)
  if (isSm.value) showPane.value = true
}

// The panel asks to open a conversation (member → message, or a search hit).
function onOpenConversation(id: string) {
  infoShow.value = false
  void store.openConversation(id)
  if (isSm.value) showPane.value = true
}

function onBack() {
  // Nav-stack feel on phones: the back arrow first closes an open info panel,
  // then (on a second press) returns to the conversation list.
  if (infoShow.value) {
    infoShow.value = false
    return
  }
  showPane.value = false
}

// After TNewChatDialog creates a conversation (startDirect/createGroup already call
// openConversation internally), only flip the mobile pane into view — do NOT call
// openConversation again (that would be a redundant double-open).
function onChatCreated() {
  if (isSm.value) showPane.value = true
}

// File upload handling
const fileInputRef = ref<HTMLInputElement | null>(null)

// Upload progress state (threaded down to the composer so the user sees a live
// bar while a file/image uploads, instead of an unexplained pause before the
// bubble appears).
const uploading = ref(false)
const uploadProgress = ref(0)
const uploadKind = ref<'image' | 'file'>('file')
const uploadName = ref('')

function onPickFile() {
  fileInputRef.value?.click()
}

// Upload a file and send it as a message — shared by the attachment picker and
// drag-and-drop. The message kind is derived from the file's MIME type (images
// render as an inline preview bubble, everything else as a download chip).
async function uploadAndSend(file: File) {
  if (!file || !store.activeId || !client) return

  // Reject an over-limit file up front with a clear message (previously the
  // upload just failed silently — no progress, no error).
  if (file.size > MAX_FILE_SIZE) {
    message?.error(interpolate(t('window.fileTooLarge'), { max: formatFileSize(MAX_FILE_SIZE) }))
    return
  }

  const isImage = !!file.type && file.type.startsWith('image/')

  uploading.value = true
  uploadProgress.value = 0
  uploadKind.value = isImage ? 'image' : 'file'
  uploadName.value = file.name
  try {
    const storageApi = useStorageApi(client)
    const result = await storageApi.upload(file, (p) => { uploadProgress.value = p })
    const uploaded = unwrapResult(result)
    if (!uploaded?.id) {
      // Surface the backend reason (e.g. size limit, 413) instead of swallowing it.
      const reason = (result as { message?: string } | null)?.message
      message?.error(reason || t('window.uploadFailed'))
      return
    }

    const contentType = isImage ? MessageContentType.Image : MessageContentType.File

    await store.sendMedia(store.activeId, {
      contentType,
      fileId: uploaded.id,
      fileName: uploaded.originalName || uploaded.fileName,
      fileSize: uploaded.size,
    })
  } catch {
    message?.error(t('window.uploadFailed'))
  } finally {
    uploading.value = false
    uploadProgress.value = 0
    uploadName.value = ''
  }
}

async function onFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) await uploadAndSend(file)
  if (input) input.value = ''
}

function onDroppedFile(file: File) {
  void uploadAndSend(file)
}
</script>

<style scoped>
.t-chat-window {
  /* Chat-scoped palette — derived from the admin theme tokens so the window
     follows the active primary colour AND light/dark mode. Accents (Send button,
     active conversation) use the theme primary; surfaces/text/borders use the
     functional tokens. Only the self-bubble keeps the signature WeChat green.
     Every value carries a light-mode fallback so the components still render
     standalone (e.g. in unit tests without the theme stylesheet). */
  --chat-green: #95ec69;
  --chat-green-text: #0d0d0d;
  --chat-send: var(--tnzi-primary-600, #158278);
  --chat-send-hover: var(--tnzi-primary-700, #19665e);
  --chat-send-disabled: rgb(var(--tnzi-primary-rgb, 13 148 136) / 0.4);
  --chat-bg: var(--tnzi-bg-deep, #f5f6f8);
  --chat-list-bg: var(--tnzi-container-bg, #ffffff);
  --chat-surface: var(--tnzi-container-bg, #ffffff);
  --chat-search-bg: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.08);
  --chat-border: var(--tnzi-border, #e6e6e6);
  --chat-hover: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.05);
  --chat-active: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.10);
  --chat-system-bg: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.05);
  --chat-text: var(--tnzi-base-text, #1f1f1f);
  --chat-text-2: var(--tnzi-base-text-muted, #6f6f6f);
  --chat-text-3: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.45);

  display: grid;
  /* 250px conversation list + 590px conversation pane = 840px base width.
     The pane is the flexible column so the list keeps its fixed footprint. */
  grid-template-columns: 250px minmax(0, 1fr);
  /* Row 1 = window title bar (drag + window controls); row 2 = the panes.
     Bounding row 2 to the window height keeps the right pane's flex layout
     (header / scrolling messages / composer) constrained instead of the
     content height pushing the composer out of the clipped box. */
  grid-template-rows: 32px minmax(0, 1fr);
  /* Responsive: shrink with the viewport instead of overflowing it. */
  width: min(840px, 94vw);
  height: min(670px, 90vh);
  background: var(--chat-bg);
  /* Follow the theme's radius (the Theme Drawer writes --tnzi-admin-radius-lg
     onto :root, which cascades to this teleported modal). */
  border-radius: var(--tnzi-admin-radius-lg, 12px);
  overflow: hidden;
  box-shadow: var(--tnzi-shadow-drawer, 0 12px 48px rgba(0, 0, 0, 0.22));
  transition: width 0.18s ease, height 0.18s ease;
}

/* Maximized: near-full-viewport (desktop only — phones are already full-screen). */
.t-chat-window--max {
  width: 96vw;
  height: 92vh;
}

.t-chat-window__left,
.t-chat-window__right {
  min-width: 0;
  min-height: 0;
  height: 100%;
}

/* The conversation list owns the full height of the left column (rows 1+2) so
   its avatar/search row reaches the very top — no empty strip above it. */
.t-chat-window__left {
  grid-column: 1;
  grid-row: 1 / -1;
}

.t-chat-window__right {
  grid-column: 2;
  grid-row: 2;
}

/* ── Window title bar (over the conversation pane only) ──────────────────── */
.t-chat-window__titlebar {
  grid-column: 2;
  grid-row: 1;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding-right: 6px;
  background: var(--chat-bg);
  cursor: move;
}

.t-chat-window__winctl {
  display: flex;
  align-items: center;
  gap: 2px;
  cursor: default;
}

.t-chat-window__winbtn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 24px;
  border: none;
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  color: var(--chat-text-2, #6f6f6f);
  transition: background 0.12s, color 0.12s;
}

.t-chat-window__winbtn:hover {
  background: var(--chat-hover, #e8e8e8);
  color: var(--chat-text, #1f1f1f);
}

.t-chat-window__winbtn--close:hover {
  background: #e64340;
  color: #fff;
}

@media (max-width: 768px) {
  .t-chat-window,
  .t-chat-window--max {
    grid-template-columns: 1fr;
    width: 96vw;
    height: 92vh;
    /* Positioning context for the push-transition (children go absolute while
       sliding). */
    position: relative;
  }

  /* Single column on phones: the title bar spans the top (it holds the close
     button), with the list/pane stacked below it in row 2 (the desktop layout
     puts the list in column 1 spanning both rows — override that here). */
  .t-chat-window__titlebar {
    grid-column: 1;
    grid-row: 1;
  }

  .t-chat-window__left {
    grid-column: 1;
    grid-row: 2;
  }

  .t-chat-window__right {
    grid-column: 1;
    grid-row: 2;
  }

  /* ── iOS-style push between list ↔ conversation pane ────────────────────── */
  .t-chat-list-enter-active,
  .t-chat-list-leave-active,
  .t-chat-pane-enter-active,
  .t-chat-pane-leave-active {
    position: absolute;
    top: 32px;            /* below the title bar */
    left: 0;
    right: 0;
    bottom: 0;
    transition: transform 0.3s cubic-bezier(0.32, 0.72, 0, 1);
    will-change: transform;
  }

  /* Pane rides on top (with a soft leading shadow); list sits underneath. */
  .t-chat-pane-enter-active,
  .t-chat-pane-leave-active {
    z-index: 2;
    box-shadow: -8px 0 24px rgb(0 0 0 / 0.12);
  }

  .t-chat-list-enter-active,
  .t-chat-list-leave-active {
    z-index: 1;
  }

  /* Pane enters from / leaves to the right edge. */
  .t-chat-pane-enter-from,
  .t-chat-pane-leave-to {
    transform: translateX(100%);
  }

  /* List parallax-shifts left as the pane covers it (and back on return). */
  .t-chat-list-enter-from,
  .t-chat-list-leave-to {
    transform: translateX(-28%);
  }
}
</style>
