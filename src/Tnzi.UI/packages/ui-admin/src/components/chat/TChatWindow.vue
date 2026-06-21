<template>
  <NModal
    :show="show"
    :mask-closable="true"
    transform-origin="center"
    @update:show="emit('update:show', $event)"
  >
    <div class="t-chat-window" :class="{ 't-chat-window--max': maximized }">
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
        :maximized="maximized"
        :show-maximize="!isSm"
        @send="(text) => store.activeId && store.sendText(store.activeId, text)"
        @pick-file="onPickFile"
        @toggle-info="infoShow = !infoShow"
        @update:info-show="infoShow = $event"
        @toggle-maximize="maximized = !maximized"
        @panel-changed="() => store.fetchConversations()"
        @open-conversation="onOpenConversation"
        @back="onBack"
        @close="emit('update:show', false)"
      />
    </div>
  </NModal>

  <!-- New chat dialog -->
  <TNewChatDialog
    :show="newChatShow"
    @update:show="newChatShow = $event"
    @created="onChatCreated"
  />

  <!-- Hidden file input for media uploads -->
  <input
    ref="fileInputRef"
    type="file"
    :accept="fileInputAccept"
    style="display: none"
    @change="onFileSelected"
  />
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NModal } from 'naive-ui'
import { MessageContentType } from '@tnzi/core/services/chat'
import { useStorageApi } from '@tnzi/core/services/storage'
import { useChatStore } from '../../stores/useChatStore'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useBreakpoint } from '../../headless/useBreakpoint'
import { unwrapResult } from '../../services/_mappers'
import TConversationList from './TConversationList.vue'
import TConversationPane from './TConversationPane.vue'
import TNewChatDialog from './TNewChatDialog.vue'

const props = defineProps<{ show: boolean }>()
const emit = defineEmits<{ 'update:show': [v: boolean] }>()

const store = useChatStore()
const auth = useAdminAuthStore()
const client = useAdminClient(false)
const { isSm } = useBreakpoint()

const showPane = ref(false)
const newChatShow = ref(false)
const infoShow = ref(false)
// Maximize the whole window to near-full-viewport and back to the 840×670 base.
const maximized = ref(false)

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
const fileInputAccept = ref('*/*')
let pendingPickType: 'image' | 'file' = 'file'

// Upload progress state (threaded down to the composer so the user sees a live
// bar while a file/image uploads, instead of an unexplained pause before the
// bubble appears).
const uploading = ref(false)
const uploadProgress = ref(0)
const uploadKind = ref<'image' | 'file'>('file')
const uploadName = ref('')

function onPickFile(type: 'image' | 'file') {
  pendingPickType = type
  fileInputAccept.value = type === 'image' ? 'image/*' : '*/*'
  fileInputRef.value?.click()
}

async function onFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file || !store.activeId || !client) {
    if (input) input.value = ''
    return
  }

  uploading.value = true
  uploadProgress.value = 0
  uploadKind.value = pendingPickType
  uploadName.value = file.name
  try {
    const storageApi = useStorageApi(client)
    const result = await storageApi.upload(file, (p) => { uploadProgress.value = p })
    const uploaded = unwrapResult(result)
    if (!uploaded?.id) return

    const contentType = pendingPickType === 'image'
      ? MessageContentType.Image
      : MessageContentType.File

    await store.sendMedia(store.activeId, {
      contentType,
      fileId: uploaded.id,
      fileName: uploaded.originalName || uploaded.fileName,
      fileSize: uploaded.size,
    })
  } finally {
    uploading.value = false
    uploadProgress.value = 0
    uploadName.value = ''
    if (input) input.value = ''
  }
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
  /* Bound the single row to the window height so the right pane's flex layout
     (header / scrolling messages / composer) is constrained instead of the
     content height pushing the composer out of the clipped box. */
  grid-template-rows: minmax(0, 1fr);
  /* Responsive: shrink with the viewport instead of overflowing it. */
  width: min(840px, 94vw);
  height: min(670px, 90vh);
  background: var(--chat-bg);
  border-radius: 10px;
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

@media (max-width: 768px) {
  .t-chat-window,
  .t-chat-window--max {
    grid-template-columns: 1fr;
    width: 96vw;
    height: 92vh;
  }
}
</style>
