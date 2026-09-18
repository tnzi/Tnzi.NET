import { describe, it, expect, vi } from 'vitest'
import { createChatBridge } from '../../../src/services/bridges/chat-bridge'

function mockBroadcastApi() {
  return {
    broadcast: vi.fn(async () => 3),
  }
}

describe('chat-bridge', () => {
  it('exposes a broadcast method', () => {
    const bridge = createChatBridge({ broadcastApi: mockBroadcastApi() as never })
    expect(typeof bridge.broadcast).toBe('function')
  })

  it('broadcast calls broadcastApi.broadcast with the dto and returns count', async () => {
    const api = mockBroadcastApi()
    const bridge = createChatBridge({ broadcastApi: api as never })
    const dto = { content: 'Hello everyone!', roleIds: ['admin', 'user'] }
    const count = await bridge.broadcast(dto)
    expect(api.broadcast).toHaveBeenCalledWith(dto)
    expect(count).toBe(3)
  })

  it('broadcast passes dto with userIds when targeting specific users', async () => {
    const api = mockBroadcastApi()
    const bridge = createChatBridge({ broadcastApi: api as never })
    const dto = { content: 'Hi!', userIds: ['u1', 'u2'] }
    await bridge.broadcast(dto)
    expect(api.broadcast).toHaveBeenCalledWith(dto)
  })

  it('broadcast with no roleIds/userIds sends to all (global broadcast)', async () => {
    const api = mockBroadcastApi()
    const bridge = createChatBridge({ broadcastApi: api as never })
    const dto = { content: 'System announcement' }
    const count = await bridge.broadcast(dto)
    expect(api.broadcast).toHaveBeenCalledWith(dto)
    expect(typeof count).toBe('number')
  })

  it('broadcast rejects with the server message on a failed envelope', async () => {
    // HttpClient RESOLVES a refusal; bare unwrap turned it into `undefined` and the
    // dialog toasted "sent" and cleared the composed message.
    const api = { broadcast: vi.fn(async () => ({ succeeded: false, success: false, code: 400, data: null, message: 'broadcast blocked by policy' })) }
    const bridge = createChatBridge({ broadcastApi: api as never })
    await expect(bridge.broadcast({ content: 'x' })).rejects.toThrow('broadcast blocked by policy')
  })

  // The Overview page has exactly one failure channel - the catch around
  // `bridge.statistics()` / `bridge.presence()`. If the bridge resolves a
  // refusal (403 for a role without chat.session.view, 500) to `null`, the page
  // renders blank KPIs and an empty presence table, indistinguishable from
  // "no conversations yet".
  it('statistics rejects with the server message on a failed envelope', async () => {
    const refused = { succeeded: false, success: false, code: 403, data: null, message: 'Permission denied: chat.session.view' }
    const adminApi = { getStatistics: vi.fn(async () => refused), getPresenceOverview: vi.fn(async () => refused) }
    const bridge = createChatBridge({ adminApi: adminApi as never })
    await expect(bridge.statistics()).rejects.toThrow('chat.session.view')
  })

  it('presence rejects with the server message on a failed envelope', async () => {
    const refused = { succeeded: false, success: false, code: 403, data: null, message: 'Permission denied: chat.session.view' }
    const adminApi = { getStatistics: vi.fn(async () => refused), getPresenceOverview: vi.fn(async () => refused) }
    const bridge = createChatBridge({ adminApi: adminApi as never })
    await expect(bridge.presence({ onlineOnly: false })).rejects.toThrow('chat.session.view')
  })

  it('statistics and presence still hand back the payload of a succeeded envelope', async () => {
    const adminApi = {
      getStatistics: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: { totalConversations: 7 } })),
      getPresenceOverview: vi.fn(async () => ({ succeeded: true, success: true, code: 200, data: { onlineCount: 2, items: [] } })),
    }
    const bridge = createChatBridge({ adminApi: adminApi as never })
    expect(await bridge.statistics()).toEqual({ totalConversations: 7 })
    expect(await bridge.presence({ onlineOnly: true })).toEqual({ onlineCount: 2, items: [] })
    expect(adminApi.getPresenceOverview).toHaveBeenCalledWith({ onlineOnly: true })
  })

  it('bridge with no deps rejects on broadcast', async () => {
    const bridge = createChatBridge()
    await expect(bridge.broadcast({ content: 'test' })).rejects.toThrow()
  })
})
