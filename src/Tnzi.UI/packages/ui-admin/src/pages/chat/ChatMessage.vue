<template>
  <!--
    ChatMessage page — Phase 3.30
    Admin view of chat messages. Read-only (no create/update from admin).
    When route query has ?sessionId=..., the page auto-filters by session.
    Backend fields from MessageListItemDto (admin query).
    The plan columns (sessionId/senderId/content/messageType/sentAt) are
    display labels; real data uses senderId, messageType, creationTime.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="chatMessageColumns"
    :title="title"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="chatMessageFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute } from 'vue-router'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createChatBridge } from '../../services/bridges/chat-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { chatMessageColumns, chatMessageFormSchema } from './chat-message-config'
import { translatePageKey } from '../_shared/translate'
import type { MessageListItemDto } from '@tnzi/core/services/chat'

const title = 'Chat Messages'
const route = useRoute()
const bridge = createChatBridge({ client: useAdminClient() })

// Admin view is read-only; messages are created by users only.
const readOnlyFn = async (): Promise<never> => { throw new Error('Chat messages are read-only from admin') }

const crud = useCrudPage<MessageListItemDto>({
  pageId: 'chat.messages',
  columns: chatMessageColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.messages.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: (ids) => bridge.messages.delete(ids.map(String)),
})


// When the route has ?sessionId=..., inject it as an initial filter so
// the page auto-scopes to messages belonging to that session.
onMounted(() => {
  const sessionId = route.query.sessionId
  if (sessionId && typeof sessionId === 'string') {
    crud.setFilters({ sessionId })
  }
  crud.refresh().catch(() => undefined)
})

const t = (key: string) => translatePageKey('chat.messages', key)
</script>
