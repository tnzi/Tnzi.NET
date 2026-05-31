/**
 * Channels bridge — wraps `/admin/channels/*` + `/admin/gateway/*` exposed by
 * `Tnzi.AI.Channels`. The module is an optional sub-module of Tnzi.AI; when
 * not loaded the backend returns 404 and the bridge surfaces empty values
 * so the page renders an "unavailable" state.
 *
 * DTOs + the raw API factory live in `@tnzi/core/services/ai` (`channels.ts`).
 * This bridge delegates to `useChannelsAdminApi(client)` and adds the tolerant
 * `unwrap` + empty-on-404 shaping the pages expect.
 */
import type { HttpClient } from '@tnzi/core/http'
import { useChannelsAdminApi } from '@tnzi/core/services/ai'
import { unwrapResult as unwrap } from '../_mappers'

export type {
  ChannelAdapterDto,
  ChannelModuleStatusDto,
  GatewayStatusDto,
  GatewayConnectionInfo,
  SessionBindingRuleDto,
} from '@tnzi/core/services/ai'

import type {
  ChannelAdapterDto,
  ChannelModuleStatusDto,
  GatewayStatusDto,
  GatewayConnectionInfo,
  SessionBindingRuleDto,
} from '@tnzi/core/services/ai'

export interface ChannelsBridgeDeps {
  client?: HttpClient
}

export interface ChannelsBridge {
  channels: {
    getStatus(): Promise<ChannelModuleStatusDto | null>
    getAdapters(): Promise<ChannelAdapterDto[]>
  }
  gateway: {
    getStatus(): Promise<GatewayStatusDto | null>
    getConnections(): Promise<GatewayConnectionInfo[]>
    getBindings(): Promise<SessionBindingRuleDto[]>
  }
}

export function createChannelsBridge(deps: ChannelsBridgeDeps = {}): ChannelsBridge {
  const { client } = deps

  if (!client) {
    const noOp = () => Promise.reject(new Error('createChannelsBridge: no HttpClient provided'))
    return {
      channels: { getStatus: noOp as never, getAdapters: noOp as never },
      gateway: { getStatus: noOp as never, getConnections: noOp as never, getBindings: noOp as never },
    }
  }

  const api = useChannelsAdminApi(client)

  return {
    channels: {
      getStatus: async () =>
        unwrap<ChannelModuleStatusDto | null>(await api.getStatus()),
      getAdapters: async () =>
        unwrap<ChannelAdapterDto[]>(await api.getAdapters()) ?? [],
    },
    gateway: {
      getStatus: async () =>
        unwrap<GatewayStatusDto | null>(await api.getGatewayStatus()),
      getConnections: async () =>
        unwrap<GatewayConnectionInfo[]>(await api.getGatewayConnections()) ?? [],
      getBindings: async () =>
        unwrap<SessionBindingRuleDto[]>(await api.getGatewayBindings()) ?? [],
    },
  }
}
