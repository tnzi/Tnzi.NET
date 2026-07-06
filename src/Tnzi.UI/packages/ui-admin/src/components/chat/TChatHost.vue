<template>
  <template v-if="client">
    <TChatLauncher :unread-count="store.totalUnread" @open="show = true" />
    <TChatWindow v-model:show="show" />
  </template>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { createChatImBridge } from '../../services/bridges/chat-im-bridge'
import { useChatRealtime } from '../../headless/useChatRealtime'
import { useNotificationSound } from '../../headless/useNotificationSound'
import { useAdminClient } from '../../plugin/client'
import { useAdminChatConfig } from '../../plugin/chatConfig'
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
  const chatConfig = useAdminChatConfig()

  store.init(bridge)

  const realtime = useChatRealtime({
    client,
    store,
    // Optional hub URL override (e.g. '/api/hubs/chat' under a sub-path).
    // Undefined when unset, so useChatRealtime falls back to '/hubs/chat'.
    hubUrl: chatConfig?.hubUrl,
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
      if (store.windowVisible && p.conversationId === store.activeId) {
        // applyIncomingMessage already appended the body incrementally — just
        // clear the unread since the user is looking at this thread. Requires
        // the window to be VISIBLE: activeId survives closing the window, and
        // without this guard a closed window silently marked messages read
        // (no badge, no sound after the first open/close cycle).
        void store.markRead(p.conversationId)
      } else if (!conv.isMuted) {
        sound.play()
      }
    },
    onConversationChanged: () => { void store.fetchConversations() },
  })

  // Mirror the window's open state into the store so incoming-message logic
  // can tell "conversation selected" apart from "conversation on screen".
  watch(show, (open) => {
    store.setWindowVisible(open)
    if (open && store.activeId) {
      // Reopening lands on the previously active thread with any new messages
      // already rendered, so clear its unread right away.
      const conv = store.conversations.find(c => c.id === store.activeId)
      if (conv?.unreadCount) void store.markRead(store.activeId)
    }
  })

  onMounted(async () => {
    // Deployment feature config first: it decides whether presence is loaded at
    // all and seeds the notification-sound default (users can still mute
    // per-conversation on top of it).
    await store.loadConfig()
    sound.setEnabled(store.config.enableMessageSound)
    await store.fetchConversations()
    if (store.config.enablePresence) {
      await store.loadMyStatus().catch(() => undefined)
      const peerIds = store.conversations.map(c => c.peerUserId).filter(Boolean) as string[]
      await store.loadPresence(peerIds).catch(() => undefined)
    }
    await realtime.start()
  })

  onUnmounted(() => {
    store.setWindowVisible(false)
    void realtime.stop()
  })
}
</script>
