import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

interface RetryPolicy {
  nextRetryDelayInMilliseconds(context: { previousRetryCount: number; elapsedMilliseconds: number; retryReason: Error }): number | null
}

const fake = vi.hoisted(() => ({
  state: 'Disconnected',
  start: null as unknown as ReturnType<typeof vi.fn>,
  stop: null as unknown as ReturnType<typeof vi.fn>,
  closeHandlers: [] as Array<(error?: Error) => void>,
  reconnectArg: undefined as unknown,
  tokenFactory: undefined as undefined | (() => string | Promise<string>),
}))

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl(_url: string, options: { accessTokenFactory?: () => string | Promise<string> }) {
      fake.tokenFactory = options.accessTokenFactory
      return this
    }
    withAutomaticReconnect(arg: unknown) {
      fake.reconnectArg = arg
      return this
    }
    configureLogging() { return this }
    build() {
      return {
        on: vi.fn(),
        off: vi.fn(),
        start: (...args: unknown[]) => fake.start(...args),
        stop: (...args: unknown[]) => fake.stop(...args),
        onclose: (handler: (error?: Error) => void) => { fake.closeHandlers.push(handler) },
        get state() { return fake.state },
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

import { createHubClient } from '../../src/services/signalr/hub-client'

function jwt(payload: Record<string, unknown>): string {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
  return `${encode({ alg: 'none' })}.${encode(payload)}.sig`
}

const NOW = new Date('2026-10-11T10:00:00Z').getTime()

/**
 * A hub that is down for longer than the reconnect schedule, a hub that is
 * unreachable at sign-in, or a laptop that sleeps past the token lifetime used
 * to leave realtime dead (chat, presence, settings hot reload) until the page
 * was reloaded, with nothing on screen saying so.
 */
describe('createHubClient recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(NOW)
    fake.state = 'Disconnected'
    fake.closeHandlers = []
    fake.reconnectArg = undefined
    fake.tokenFactory = undefined
    fake.start = vi.fn(async () => { fake.state = 'Connected' })
    fake.stop = vi.fn(async () => { fake.state = 'Disconnected' })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('never stops reconnecting after a drop while the client is started', () => {
    createHubClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    const policy = fake.reconnectArg as RetryPolicy
    expect(typeof policy?.nextRetryDelayInMilliseconds).toBe('function')

    const delays = [0, 1, 2, 3, 4, 5, 20, 500].map((previousRetryCount) =>
      policy.nextRetryDelayInMilliseconds({ previousRetryCount, elapsedMilliseconds: 0, retryReason: new Error('down') }))
    for (const delay of delays) {
      expect(delay).not.toBeNull()
      expect(delay).toBeLessThanOrEqual(60_000)
    }
  })

  it('keeps retrying when the first start fails, and reports the failure to the caller', async () => {
    fake.start = vi.fn()
      .mockRejectedValueOnce(new Error('unreachable'))
      .mockRejectedValueOnce(new Error('unreachable'))
      .mockImplementation(async () => { fake.state = 'Connected' })
    const client = createHubClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })

    await expect(client.start()).rejects.toThrow('unreachable')
    await vi.advanceTimersByTimeAsync(60_000)

    expect(fake.start).toHaveBeenCalledTimes(3)
    expect(client.isConnected()).toBe(true)
  })

  it('restarts when the connection closes for good while started', async () => {
    const client = createHubClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })
    await client.start()

    fake.state = 'Disconnected'
    for (const handler of fake.closeHandlers) handler(new Error('server closed'))
    await vi.advanceTimersByTimeAsync(60_000)

    expect(fake.start).toHaveBeenCalledTimes(2)
  })

  it('stops retrying once the caller stops the client', async () => {
    fake.start = vi.fn(async () => { throw new Error('unreachable') })
    const client = createHubClient({ url: '/hubs/chat', accessTokenFactory: () => 't' })

    await expect(client.start()).rejects.toThrow()
    await client.stop()
    for (const handler of fake.closeHandlers) handler()
    await vi.advanceTimersByTimeAsync(10 * 60_000)

    expect(fake.start).toHaveBeenCalledTimes(1)
  })

  it('refreshes an expired token before handing it to a (re)connect', async () => {
    const expired = jwt({ exp: Math.floor(NOW / 1000) - 60 })
    const refreshAccessToken = vi.fn(async () => 'fresh')
    createHubClient({ url: '/hubs/chat', accessTokenFactory: () => expired, refreshAccessToken })

    await expect(Promise.resolve(fake.tokenFactory!())).resolves.toBe('fresh')
    expect(refreshAccessToken).toHaveBeenCalledTimes(1)
  })

  it('hands over a valid token as is, and accepts an async factory', async () => {
    const valid = jwt({ exp: Math.floor(NOW / 1000) + 3600 })
    const refreshAccessToken = vi.fn(async () => 'fresh')
    createHubClient({ url: '/hubs/chat', accessTokenFactory: async () => valid, refreshAccessToken })

    await expect(Promise.resolve(fake.tokenFactory!())).resolves.toBe(valid)
    expect(refreshAccessToken).not.toHaveBeenCalled()
  })

  it('falls back to the current token when the refresh yields nothing', async () => {
    const expired = jwt({ exp: Math.floor(NOW / 1000) - 60 })
    createHubClient({ url: '/hubs/chat', accessTokenFactory: () => expired, refreshAccessToken: async () => null })

    await expect(Promise.resolve(fake.tokenFactory!())).resolves.toBe(expired)
  })
})
