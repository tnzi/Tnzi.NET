<template>
  <template v-if="client">
    <TChatLauncher :unread-count="store.totalUnread" @open="show = true" />
    <TChatWindow v-model:show="show" />
  </template>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { createChatImBridge } from '../../services/bridges/chat-im-bridge'
import { useChatRealtime } from '../../headless/useChatRealtime'
import { useNotificationSound } from '../../headless/useNotificationSound'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useChatStore } from '../../stores/useChatStore'
import TChatLauncher from './TChatLauncher.vue'
import TChatWindow from './TChatWindow.vue'

// Inert gate: no client = no orchestration, no render
const client = useAdminClient(false)

const store = useChatStore()
const auth = useAdminAuthStore()
const show = ref(false)

// Only set up orchestration when a client is available
if (client) {
  const bridge = createChatImBridge({ client })
  const sound = useNotificationSound()

  store.init(bridge)

  const realtime = useChatRealtime({
    client,
    store,
    // Read the freshest token from the HttpClient (it self-refreshes on 401),
    // falling back to the auth store. The store token is only written at login
    // and never re-synced, so reading it alone would go stale after the access
    // token expires (~1h) and SignalR reconnects would 401 → realtime dies.
    getToken: () => client.getAccessToken() ?? auth.token ?? '',
    getUserId: () => auth.userInfo?.id,
    onNewMessage: (p) => {
      const conv = store.conversations.find(c => c.id === p.conversationId)
      if (!conv) {
        // Message for a conversation not yet in the list (new direct/group) —
        // refetch so it appears; no sound for the very first surfacing.
        void store.fetchConversations()
        return
      }
      if (p.conversationId === store.activeId) {
        // applyIncomingMessage already appended the body incrementally — just
        // clear the unread since the user is looking at this thread.
        void store.markRead(p.conversationId)
      } else if (!conv.isMuted) {
        sound.play()
      }
    },
    onConversationChanged: () => { void store.fetchConversations() },
  })

  onMounted(async () => {
    await store.fetchConversations()
    await store.loadMyStatus().catch(() => undefined)
    const peerIds = store.conversations.map(c => c.peerUserId).filter(Boolean) as string[]
    await store.loadPresence(peerIds).catch(() => undefined)
    await realtime.start()
  })

  onUnmounted(() => { void realtime.stop() })
}
</script>
