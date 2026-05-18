<template>
  <!--
    ChatSession page — Phase 3.29 / 2026-04-14 unstub
    Wired to /admin/chat-sessions (DefaultChatSessionAdminController in
    Tnzi.Chat). Admin-curated session groupings; row action "View messages"
    still navigates to /admin/chat/messages?sessionId={row.id} so the
    message page can filter by session id via messages.fetchBySession.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="chatSessionColumns"
    :title="title"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="chatSessionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t">
        <template #prepend>
          <NButton size="small" type="info" ghost @click="viewMessages(row)">Messages</NButton>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { NButton } from 'naive-ui'
import type { ChatSessionListItemDto } from '@tnzi/core/services/chat'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createChatBridge } from '../../services/bridges/chat-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { chatSessionColumns, chatSessionFormSchema } from './chat-session-config'
import { translatePageKey } from '../_shared/translate'

const title = 'title'
const router = useRouter()
const bridge = createChatBridge({ client: useAdminClient() })

/**
 * Admin-curated chat session groupings. Row shape is ChatSessionListItemDto
 * on fetch, ChatSessionDto on create/update (structurally compatible for the
 * fields the list view consumes — bridge handles the create/update DTO mapping).
 */
const crud = useCrudPage<ChatSessionListItemDto, string>({
  pageId: 'chat.sessions',
  columns: chatSessionColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.sessions.fetch(query),
  createData: (data) => bridge.sessions.create(data),
  updateData: (id, data) => bridge.sessions.update(id, data),
  deleteData: (ids) => bridge.sessions.delete(ids),
})


crud.refresh().catch(() => undefined)

function viewMessages(row: ChatSessionListItemDto): void {
  const sessionId = String(row.id ?? '')
  router.push({ path: '/admin/chat/messages', query: { sessionId } })
}

const t = (key: string) => translatePageKey('chat.sessions', key)
</script>
