import { describe, it, expect, vi, beforeEach } from 'vitest'

const onSpy = vi.fn(), offSpy = vi.fn(), startSpy = vi.fn(async () => {}), stopSpy = vi.fn(async () => {})
/** Mutable so a test can put the fake connection in any HubConnectionState. */
let connectionState = 'Disconnected'
vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      return {
        on: onSpy,
        off: offSpy,
        start: startSpy,
        stop: stopSpy,
        get state() { return connectionState },
      }
    }
  }
  return {
    HubConnectionBuilder,
    LogLevel: { Information: 2, Warning: 3 },
    HubConnectionState: {
      Disconnected: 'Disconnected',
      Connecting: 'Connecting',
      Connected: 'Connected',
      Disconnecting: 'Disconnecting',
      Reconnecting: 'Reconnecting',
    },
  }
})

import { createChatSignalRClient } from '../../src/services/chat/signalr-client'

beforeEach(() => { onSpy.mockClear(); startSpy.mockClear(); connectionState = 'Disconnected' })

describe('createChatSignalRClient', () => {
  it('start() calls connection.start', async () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    await c.start()
    expect(startSpy).toHaveBeenCalled()
  })
  it('start() is a no-op while already Connected', async () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    connectionState = 'Connected'
    await c.start()
    expect(startSpy).not.toHaveBeenCalled()
  })
  it('start() does not call connection.start while Connecting or Reconnecting', async () => {
    // signalr throws "Cannot start a HubConnection that is not in the
    // 'Disconnected' state." for every non-Disconnected state, not just Connected.
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    for (const state of ['Connecting', 'Reconnecting', 'Disconnecting']) {
      connectionState = state
      await c.start()
    }
    expect(startSpy).not.toHaveBeenCalled()
  })
  it('concurrent start() calls share a single connection.start', async () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    await Promise.all([c.start(), c.start(), c.start()])
    expect(startSpy).toHaveBeenCalledTimes(1)
  })
  it('isConnected() reflects the connection state', () => {
    const c = createChatSignalRClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    expect(c.isConnected()).toBe(false)
    connectionState = 'Connected'
    expect(c.isConnected()).toBe(true)
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
    const payload = { userId: 'u1', status: 'Online', lastSeenAt: '2026-06-20T00:00:00Z' }
    const registeredHandler = onSpy.mock.calls.find(([evt]) => evt === 'Chat.PresenceChanged')?.[1]
    registeredHandler?.(payload)
    expect(h).toHaveBeenCalledWith(payload)
  })
})
