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

  it('bridge with no deps rejects on broadcast', async () => {
    const bridge = createChatBridge()
    await expect(bridge.broadcast({ content: 'test' })).rejects.toThrow()
  })
})
