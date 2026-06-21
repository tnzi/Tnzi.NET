import {
  useChatBroadcastApi,
  type BroadcastDto,
} from '@tnzi/core/services/chat'
import { unwrapResult as unwrap } from '../_mappers'

type HttpClient = Parameters<typeof useChatBroadcastApi>[0]

export interface ChatBridgeDeps {
  /** Production path: provide HttpClient; bridge builds API internally. */
  client?: HttpClient
  /** Test path: inject mock broadcast API directly. */
  broadcastApi?: ReturnType<typeof useChatBroadcastApi>
}

export interface ChatBridge {
  /** Send a broadcast message to all users or a targeted subset. */
  broadcast(dto: BroadcastDto): Promise<number>
}

export function createChatBridge(deps: ChatBridgeDeps = {}): ChatBridge {
  const broadcastApi = deps.broadcastApi ?? (deps.client ? useChatBroadcastApi(deps.client) : null)

  if (!broadcastApi) {
    return {
      broadcast: () =>
        Promise.reject(new Error('chat-bridge: broadcast — no HttpClient (deps.client) provided')),
    }
  }

  const api = broadcastApi

  return {
    broadcast: async (dto: BroadcastDto): Promise<number> =>
      unwrap<number>(await api.broadcast(dto)),
  }
}
