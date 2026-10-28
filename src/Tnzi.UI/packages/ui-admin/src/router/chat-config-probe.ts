import { watch } from 'vue'
import type { HttpClient } from '@tnzi/core/http'
import { useChatStore } from '../stores/useChatStore'
import { createChatImBridge } from '../services/bridges/chat-im-bridge'

export interface ChatConfigProbeOptions {
  /** Chat could run here: the consumer opted in and the backend loaded the Chat module. */
  possible: () => boolean
  /** A session is signed in. Without one the probe would only answer 401. */
  signedIn: () => boolean
  /** The admin client the chat bridge talks through; no client, no probe. */
  client: HttpClient | undefined
}

/**
 * Keep the built-in chat's per-user config (`GET /chat/config`) probed for
 * whoever is signed in. Chat is deny-by-default: the launcher only appears
 * once the probe confirms this user holds `chat.use`, so a disabled user
 * never mounts the chat host at all.
 *
 * Re-probes when the store is claimed for another user. A user switch
 * without a sign-out resets the chat store, which puts the config back to
 * disabled; the shell stays mounted and "could chat run" does not change, so
 * a probe tied to that alone never ran again and the new user's chat stayed
 * off until the shell was mounted again. `ensureConfig()` asks once per store
 * session, so the first claim after mount does not probe a second time.
 *
 * The chat store's bridge is initialised here, not only by the chat host: the
 * probe runs before the host exists, and without a bridge `loadConfig()`
 * would fail before asking. The host re-initialises the same stateless bridge
 * when it mounts.
 */
export function useChatConfigProbe(options: ChatConfigProbeOptions): void {
  const chatStore = useChatStore()
  watch(
    () => [options.possible() && options.signedIn() && !!options.client, chatStore.ownerId] as const,
    ([ready]) => {
      if (!ready || !options.client) return
      chatStore.init(createChatImBridge({ client: options.client }))
      void chatStore.ensureConfig()
    },
    { immediate: true },
  )
}
