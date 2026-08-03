import { createChatSignalRClient } from '@tnzi/core/services/chat'
import type { NewMessagePayload, MessageReadPayload, ConversationChangedPayload, PresenceChangedPayload } from '@tnzi/core/services/chat'
import type { useChatStore } from '../stores/useChatStore'

export interface UseChatRealtimeOptions {
  client: unknown            // reserved (token/base resolution); kept for symmetry
  store: ReturnType<typeof useChatStore>
  /** Hub URL, or a getter evaluated at `start()` time. Default '/hubs/chat'. */
  hubUrl?: string | (() => string | undefined)
  getToken: () => string
  getUserId: () => string | undefined
  /** Called on an incoming message from another user (UI plays sound / shows toast). */
  onNewMessage?: (payload: NewMessagePayload) => void
  /** Called when the conversation set changes (created/member/dissolved) so UI can refetch. */
  onConversationChanged?: (payload: ConversationChangedPayload) => void
}

export function useChatRealtime(opts: UseChatRealtimeOptions) {
  // Built on first start() - see useSettingsRealtime for why the URL cannot be
  // resolved at setup time.
  let signal: ReturnType<typeof createChatSignalRClient> | null = null

  function resolveUrl(): string {
    const configured = typeof opts.hubUrl === 'function' ? opts.hubUrl() : opts.hubUrl
    return configured ?? '/hubs/chat'
  }

  const onNew = (raw: unknown) => {
    const p = raw as NewMessagePayload
    opts.store.applyIncomingMessage(p, opts.getUserId())
    // System / broadcast messages carry senderId=null. Treat "not my own message"
    // as from-other so a broadcast raises the unread badge + notification sound +
    // surfaces a new System conversation, exactly like an incoming DM.
    const fromOther = p.senderId !== opts.getUserId()
    if (fromOther) opts.onNewMessage?.(p)
  }
  const onRead = (raw: unknown) => opts.store.applyRead(raw as MessageReadPayload)
  const onChanged = (raw: unknown) => opts.onConversationChanged?.(raw as ConversationChangedPayload)
  const onPresence = (raw: unknown) => opts.store.applyPresenceChange(raw as PresenceChangedPayload)

  async function start() {
    signal ??= createChatSignalRClient({
      url: resolveUrl(),
      accessTokenFactory: () => opts.getToken(),
    })
    signal.on('Chat.NewMessage', onNew)
    signal.on('Chat.MessageRead', onRead)
    signal.on('Chat.ConversationChanged', onChanged)
    signal.on('Chat.PresenceChanged', onPresence)
    await signal.start()
  }
  async function stop() {
    if (!signal) return
    signal.off('Chat.NewMessage', onNew)
    signal.off('Chat.MessageRead', onRead)
    signal.off('Chat.ConversationChanged', onChanged)
    signal.off('Chat.PresenceChanged', onPresence)
    await signal.stop()
  }
  /** The underlying hub client, or `null` until the first `start()`. */
  function client() {
    return signal
  }
  return { start, stop, client }
}
