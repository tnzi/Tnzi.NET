<template>
  <!--
    Conversations — global conversation management (TCrudPage, read-only list +
    dissolve). Filter by type / participant / keyword; the row "view" action
    opens a right drawer with metadata + member table + message list, where an
    admin can force-recall any message. Backend: /admin/chat/conversations/*.
  -->
  <!--
    `display: contents` keeps the flex height chain unbroken: this wrapper only
    exists to host the detail NDrawer alongside TCrudPage, but a real box here
    (block, flex-grow:0) collapses the table body to header height. `contents`
    makes TCrudPage a direct flex child of the content page, like single-root
    CRUD pages. The NDrawer teleports to <body> so it is unaffected.
  -->
  <div class="t-conversations-host">
    <TCrudPage
      :state="crud"
      :all-columns="conversationColumns"
      :search-fields="conversationSearchFields"
      :title="title"
      :row-actions="rowActions"
      :translate="t"
    >
      <template #toolbarRight>
        <NButton size="small" @click="broadcastShow = true">
          <template #icon><TSvgIcon icon="mdi:bullhorn-outline" :size="16" /></template>
          {{ t('broadcast') }}
        </NButton>
      </template>
    </TCrudPage>

    <NDrawer v-model:show="detailShow" :width="640" placement="right">
      <NDrawerContent :title="t('detail.title')" closable>
        <NSpin :show="detailLoading">
          <template v-if="detail">
            <NDescriptions :column="1" label-placement="left" size="small" bordered>
              <NDescriptionsItem :label="t('detail.type')">
                {{ t(`type.${typeKey(detail.type)}`) }}
              </NDescriptionsItem>
              <NDescriptionsItem :label="t('detail.name')">{{ detail.title || '-' }}</NDescriptionsItem>
              <NDescriptionsItem :label="t('detail.owner')">{{ detail.ownerName || '-' }}</NDescriptionsItem>
              <NDescriptionsItem :label="t('detail.members')">{{ detail.memberCount }}</NDescriptionsItem>
              <NDescriptionsItem :label="t('detail.messages')">{{ detail.messageCount }}</NDescriptionsItem>
              <NDescriptionsItem v-if="detail.notice" :label="t('detail.notice')">{{ detail.notice }}</NDescriptionsItem>
            </NDescriptions>

            <div class="t-section-title">{{ t('detail.memberList') }}</div>
            <NTable size="small" :single-line="false">
              <thead>
                <tr>
                  <th>{{ t('detail.memberName') }}</th>
                  <th>{{ t('detail.role') }}</th>
                  <th>{{ t('detail.unread') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="m in detail.members" :key="m.userId">
                  <td>{{ m.name || m.userId }}</td>
                  <td>{{ m.role === 1 ? t('detail.roleOwner') : t('detail.roleMember') }}</td>
                  <td>{{ m.unreadCount }}</td>
                </tr>
              </tbody>
            </NTable>

            <div class="t-section-title">{{ t('detail.messageList') }}</div>
            <NImageGroup>
              <div class="t-msg-list">
                <div v-if="hasMoreMessages" class="t-msg-loadmore">
                  <NButton text size="small" :loading="loadingMore" @click="loadMoreMessages">
                    {{ t('detail.loadMore') }}
                  </NButton>
                </div>
                <div v-for="msg in messages" :key="msg.id" class="t-msg-item">
                  <div class="t-msg-head">
                    <span class="t-msg-sender">{{ senderLabel(msg) }}</span>
                    <span class="t-msg-time">{{ formatDateTime(msg.sentAt) }}</span>
                    <NButton text size="tiny" type="error" class="t-msg-recall" @click="recall(msg.id)">
                      {{ t('detail.recall') }}
                    </NButton>
                  </div>
                  <div class="t-msg-body">
                    <NImage
                      v-if="msg.contentType === MessageContentType.Image && msg.fileId"
                      :src="previewUrl(msg.fileId)"
                      :width="140"
                      object-fit="cover"
                      class="t-msg-image"
                    />
                    <a
                      v-else-if="msg.contentType === MessageContentType.File && msg.fileId"
                      :href="downloadUrl(msg.fileId)"
                      class="t-msg-file"
                      target="_blank"
                      rel="noopener noreferrer"
                      download
                    >
                      <TSvgIcon icon="mdi:file-download-outline" :size="18" />
                      <span class="t-msg-file__name">{{ msg.fileName || t('detail.fileMsg') }}</span>
                      <span v-if="msg.fileSize" class="t-msg-file__size">{{ formatFileSize(msg.fileSize) }}</span>
                    </a>
                    <span v-else>{{ msgPreview(msg) }}</span>
                  </div>
                </div>
                <NEmpty v-if="!messages.length" :description="t('detail.noMessages')" size="small" />
              </div>
            </NImageGroup>
          </template>
        </NSpin>
      </NDrawerContent>
    </NDrawer>

    <BroadcastDialog v-model:show="broadcastShow" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NDrawer, NDrawerContent, NDescriptions, NDescriptionsItem, NEmpty, NImage, NImageGroup, NSpin, NTable } from 'naive-ui'
import { formatDateTime, formatFileSize } from '@tnzi/core'
import { TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import BroadcastDialog from './BroadcastDialog.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/rowActions'
import { createChatBridge } from '../../services/bridges/chat-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { conversationColumns, conversationSearchFields, typeKey } from './conversations-config'
import {
  MessageContentType,
  type AdminConversationListItemDto,
  type AdminConversationDetailDto,
  type ChatMessageDto,
} from '@tnzi/core/services/chat'

const title = 'title'
const t = (key: string) => translatePageKey('chat.conversations', key)
const message = useSafeMessage()
const bridge = createChatBridge({ client: useAdminClient() })

const crud = useCrudPage<AdminConversationListItemDto, string>({
  pageId: 'chat.conversations',
  columns: conversationColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.conversations.fetch(query),
  deleteData: (ids) => bridge.conversations.delete(ids),
})
crud.refresh().catch(() => undefined)

const rowActions: RowAction<AdminConversationListItemDto>[] = [
  { key: 'view', label: 'detail.view', onClick: (row) => openDetail(row.id) },
  deleteAction(crud),
]

const broadcastShow = ref(false)
const detailShow = ref(false)
const detailLoading = ref(false)
const detail = ref<AdminConversationDetailDto | null>(null)
const messages = ref<ChatMessageDto[]>([])
const hasMoreMessages = ref(false)
const loadingMore = ref(false)
const MSG_PAGE = 20

/** Inline preview / forced-download URLs (same routes the chat window uses). */
function previewUrl(fileId: string): string {
  return `/api/files/${fileId}/preview`
}
function downloadUrl(fileId: string): string {
  return `/api/files/${fileId}/download`
}

async function openDetail(id: string): Promise<void> {
  detailShow.value = true
  detailLoading.value = true
  detail.value = null
  messages.value = []
  hasMoreMessages.value = false
  try {
    detail.value = await bridge.conversations.detail(id)
    const thread = await bridge.conversations.messages(id, { limit: MSG_PAGE })
    messages.value = thread.messages
    hasMoreMessages.value = thread.hasMore
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    detailLoading.value = false
  }
}

/** Cursor-page older messages (prepend); the thread is ascending so [0] is the earliest loaded. */
async function loadMoreMessages(): Promise<void> {
  const earliest = messages.value[0]
  if (!detail.value || !earliest) return
  loadingMore.value = true
  try {
    const thread = await bridge.conversations.messages(detail.value.id, { before: earliest.id, limit: MSG_PAGE })
    messages.value = [...thread.messages, ...messages.value]
    hasMoreMessages.value = thread.hasMore
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loadingMore.value = false
  }
}

async function recall(messageId: string): Promise<void> {
  try {
    await bridge.deleteMessage(messageId)
    messages.value = messages.value.filter((m) => m.id !== messageId)
    if (detail.value) detail.value.messageCount = Math.max(0, detail.value.messageCount - 1)
    message.success(t('detail.recalled'))
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

function senderLabel(m: ChatMessageDto): string {
  if (m.contentType === MessageContentType.System) return t('detail.systemSender')
  return m.senderName || m.senderId || '-'
}

function msgPreview(m: ChatMessageDto): string {
  if (m.contentType === MessageContentType.Image) return t('detail.imageMsg')
  if (m.contentType === MessageContentType.File) return m.fileName || t('detail.fileMsg')
  return m.content
}
</script>

<style scoped>
.t-conversations-host {
  display: contents;
}
.t-section-title {
  margin: 16px 0 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--tnzi-text-color-2, #666);
}
.t-msg-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-msg-item {
  padding: 8px 10px;
  border: 1px solid var(--tnzi-border-color, #eee);
  border-radius: var(--tnzi-admin-radius-md, 6px);
}
.t-msg-head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--tnzi-text-color-3, #999);
}
.t-msg-sender {
  font-weight: 600;
  color: var(--tnzi-text-color-2, #666);
}
.t-msg-time {
  flex: 1;
}
.t-msg-recall {
  flex: none;
}
.t-msg-body {
  margin-top: 4px;
  font-size: 13px;
  word-break: break-word;
}
.t-msg-loadmore {
  display: flex;
  justify-content: center;
  padding: 2px 0 8px;
}
.t-msg-image {
  border-radius: var(--tnzi-admin-radius-md, 6px);
  overflow: hidden;
  cursor: pointer;
}
.t-msg-file {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border: 1px solid var(--tnzi-border-color, #eee);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  text-decoration: none;
  color: var(--tnzi-text-color-2, #666);
  max-width: 100%;
}
.t-msg-file:hover {
  border-color: var(--tnzi-primary, #2080f0);
  color: var(--tnzi-primary, #2080f0);
}
.t-msg-file__name {
  word-break: break-all;
}
.t-msg-file__size {
  flex: none;
  font-size: 12px;
  color: var(--tnzi-text-color-3, #999);
}
</style>
