import { createChatSignalRClient } from '@tnzi/core/services/chat'
import type { NewMessagePayload, MessageReadPayload, ConversationChangedPayload, PresenceChangedPayload } from '@tnzi/core/services/chat'
import type { useChatStore } from '../stores/useChatStore'

export interface UseChatRealtimeOptions {
  client: unknown            // reserved (token/base resolution); kept for symmetry
  store: ReturnType<typeof useChatStore>
  hubUrl?: string            // default '/hubs/chat'
  getToken: () => string
  getUserId: () => string | undefined
  /** Called on an incoming message from another user (UI plays sound / shows toast). */
  onNewMessage?: (payload: NewMessagePayload) => void
  /** Called when the conversation set changes (created/member/dissolved) so UI can refetch. */
  onConversationChanged?: (payload: ConversationChangedPayload) => void
}

export function useChatRealtime(opts: UseChatRealtimeOptions) {
  const signal = createChatSignalRClient({
    url: opts.hubUrl ?? '/hubs/chat',
    accessTokenFactory: () => opts.getToken(),
  })

  const onNew = (raw: unknown) => {
    const p = raw as NewMessagePayload
    opts.store.applyIncomingMessage(p, opts.getUserId())
    const fromOther = p.senderId && p.senderId !== opts.getUserId()
    if (fromOther) opts.onNewMessage?.(p)
  }
  const onRead = (raw: unknown) => opts.store.applyRead(raw as MessageReadPayload)
  const onChanged = (raw: unknown) => opts.onConversationChanged?.(raw as ConversationChangedPayload)
  const onPresence = (raw: unknown) => opts.store.applyPresenceChange(raw as PresenceChangedPayload)

  async function start() {
    signal.on('Chat.NewMessage', onNew)
    signal.on('Chat.MessageRead', onRead)
    signal.on('Chat.ConversationChanged', onChanged)
    signal.on('Chat.PresenceChanged', onPresence)
    await signal.start()
  }
  async function stop() {
    signal.off('Chat.NewMessage', onNew)
    signal.off('Chat.MessageRead', onRead)
    signal.off('Chat.ConversationChanged', onChanged)
    signal.off('Chat.PresenceChanged', onPresence)
    await signal.stop()
  }
  return { start, stop, client: signal }
}
