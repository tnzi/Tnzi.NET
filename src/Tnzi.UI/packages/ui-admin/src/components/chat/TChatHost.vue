<template>
  <template v-if="client">
    <TChatLauncher
      :unread-count="store.totalUnread"
      :effect="store.config.newMessageEffect"
      :attention="attentionSeq"
      @open="show = true"
    />
    <TChatWindow v-model:show="show" />
  </template>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { createChatImBridge } from '../../services/bridges/chat-im-bridge'
import { useChatRealtime } from '../../headless/useChatRealtime'
import { useChatSound } from '../../headless/useChatSound'
import { useTitleFlash } from '../../headless/useTitleFlash'
import { translatePageKey } from '../../pages/_shared/translate'
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
// Bumped on each new message that arrives while the window is closed; drives the
// launcher icon's attention animation. Top-level so the template can bind it.
const attentionSeq = ref(0)

// Only set up orchestration when a client is available
if (client) {
  const bridge = createChatImBridge({ client })
  const sound = useChatSound()
  const titleFlash = useTitleFlash()
  const chatConfig = useAdminChatConfig()
  const t = (k: string) => translatePageKey('chat', k)

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
        // Actively viewing this thread → the short, gentle in-conversation tone
        // (still silenced when the conversation is muted). Own sends never echo
        // back (server excludes the sender), so this is always a received message.
        if (!conv.isMuted) sound.playMessage()
      } else if (!conv.isMuted) {
        // Closed window or a different thread → the longer attention tone.
        sound.playNotification()
        // Visual attention cues (in addition to the unread badge):
        //  - launcher icon animation while the window is CLOSED (user is in the app)
        //  - tab-title flash while the browser tab is UNFOCUSED (user is away)
        if (!store.windowVisible) attentionSeq.value++
        const hidden = typeof document !== 'undefined' && document.hidden
        if (store.config.flashTitleOnMessage && hidden) {
          titleFlash.flash(`(${store.totalUnread}) ${t('launcher.newMessages')}`)
        }
      }
    },
    onConversationChanged: () => { void store.fetchConversations() },
  })

  // Mirror the window's open state into the store so incoming-message logic
  // can tell "conversation selected" apart from "conversation on screen".
  watch(show, (open) => {
    store.setWindowVisible(open)
    if (open) {
      titleFlash.stop() // engaging with chat clears the tab-title flash
      if (store.activeId) {
        // Reopening lands on the previously active thread with any new messages
        // already rendered, so clear its unread right away.
        const conv = store.conversations.find(c => c.id === store.activeId)
        if (conv?.unreadCount) void store.markRead(store.activeId)
      }
    }
  })

  // Once everything is read (here or elsewhere), stop flashing the tab title.
  watch(() => store.totalUnread, (n) => { if (n === 0) titleFlash.stop() })

  // Keep the sound engine in sync with the deployment config. The initial
  // configure runs in onMounted after the first loadConfig; this watch also
  // covers a LIVE config change pushed over `/hubs/settings` (the shell calls
  // store.loadConfig() again, updating store.config) so the notification/message
  // sound presets and the master mute switch take effect without a page reload.
  watch(
    () => [store.config.enableMessageSound, store.config.notificationSound, store.config.messageSound],
    () => sound.configure({
      enabled: store.config.enableMessageSound,
      notification: store.config.notificationSound,
      message: store.config.messageSound,
    }),
  )

  onMounted(async () => {
    // Deployment feature config first: it decides whether presence is loaded at
    // all and seeds the notification-sound default (users can still mute
    // per-conversation on top of it).
    await store.loadConfig()
    // Deny-by-default guard: if the backend didn't confirm this user may use chat
    // (`chat.use`), make NO further chat calls. AdminShellRoot already gates
    // mounting on `config.enabled`, but re-checking here means TChatHost never
    // hits the 403-guarded /conversations + /presence endpoints for a disabled
    // user even if it is ever mounted in an unconfirmed state (login-transition
    // race). Without this, a denied user 403'd on every mount + crashed.
    if (!store.config.enabled) return
    sound.configure({
      enabled: store.config.enableMessageSound,
      notification: store.config.notificationSound,
      message: store.config.messageSound,
    })
    await store.fetchConversations().catch(() => undefined)
    if (store.config.enablePresence) {
      await store.loadMyStatus().catch(() => undefined)
      const peerIds = store.conversations.map(c => c.peerUserId).filter(Boolean) as string[]
      await store.loadPresence(peerIds).catch(() => undefined)
    }
    await realtime.start()
  })

  onUnmounted(() => {
    store.setWindowVisible(false)
    titleFlash.stop()
    void realtime.stop()
  })
}
</script>
