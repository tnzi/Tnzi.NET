import { describe, it, expect, vi, beforeEach } from 'vitest'

const onSpy = vi.fn(), offSpy = vi.fn(), startSpy = vi.fn(async () => {}), stopSpy = vi.fn(async () => {})
vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return { on: onSpy, off: offSpy, start: startSpy, stop: stopSpy, state: 'Disconnected' } }
  }
  return { HubConnectionBuilder, LogLevel: { Information: 2, Warning: 3 }, HubConnectionState: { Connected: 'Connected' } }
})

import { createChatSignalRClient } from '../../services/chat/signalr-client'

beforeEach(() => { onSpy.mockClear(); startSpy.mockClear() })

describe('createChatSignalRClient', () => {
  it('start() calls connection.start', async () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    await c.start()
    expect(startSpy).toHaveBeenCalled()
  })
  it('on() forwards to connection.on with the event name', () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    const h = vi.fn()
    c.on('Chat.NewMessage', h)
    expect(onSpy).toHaveBeenCalledWith('Chat.NewMessage', h)
  })
  it('on(Chat.PresenceChanged) wires handler to connection and receives payload', () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    const h = vi.fn()
    c.on('Chat.PresenceChanged', h)
    expect(onSpy).toHaveBeenCalledWith('Chat.PresenceChanged', h)
    // Simulate the connection firing the event
    const payload = { userId: 'u1', status: 1, lastSeenAt: '2026-06-20T00:00:00Z' }
    const registeredHandler = onSpy.mock.calls.find(([evt]) => evt === 'Chat.PresenceChanged')?.[1]
    registeredHandler?.(payload)
    expect(h).toHaveBeenCalledWith(payload)
  })
})
