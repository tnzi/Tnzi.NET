import { describe, it, expect, vi, beforeEach } from 'vitest'

// Hoisted so the vi.mock factory (also hoisted) can reference it.
const { mockClient } = vi.hoisted(() => ({
  mockClient: {
    on: vi.fn(),
    off: vi.fn(),
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
    isConnected: vi.fn(() => false),
    connection: {},
  },
}))

vi.mock('@tnzi/core/services/system', () => ({
  createSettingsRealtimeClient: vi.fn(() => mockClient),
}))

import { lastSettingsChange, useSettingsRealtime } from '../../src/headless/useSettingsRealtime'
import { createSettingsRealtimeClient } from '@tnzi/core/services/system'

describe('useSettingsRealtime', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('connects to /hubs/settings by default and wires the token factory', () => {
    const getToken = vi.fn(() => 'tok')
    useSettingsRealtime({ getToken, onChanged: () => {} })

    const opts = (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0]
    expect(opts.url).toBe('/hubs/settings')
    expect(opts.accessTokenFactory()).toBe('tok')
    expect(getToken).toHaveBeenCalled()
  })

  it('honours a hubUrl override', () => {
    useSettingsRealtime({ hubUrl: '/api/hubs/settings', getToken: () => '', onChanged: () => {} })
    const opts = (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0]
    expect(opts.url).toBe('/api/hubs/settings')
  })

  it('start() subscribes Settings.Changed, starts, and routes the payload to onChanged', async () => {
    const onChanged = vi.fn()
    const rt = useSettingsRealtime({ getToken: () => '', onChanged })

    await rt.start()

    expect(mockClient.start).toHaveBeenCalledOnce()
    expect(mockClient.on).toHaveBeenCalledWith('Settings.Changed', expect.any(Function))

    // Simulate an incoming broadcast and assert it reaches the consumer verbatim.
    const handler = mockClient.on.mock.calls[0][1] as (raw: unknown) => void
    handler({ key: 'Chat:AllowInvisible', isRemoval: false })
    expect(onChanged).toHaveBeenCalledWith({ key: 'Chat:AllowInvisible', isRemoval: false })
  })

  it('stop() unsubscribes and stops', async () => {
    const rt = useSettingsRealtime({ getToken: () => '', onChanged: () => {} })
    await rt.start()
    await rt.stop()

    expect(mockClient.off).toHaveBeenCalledWith('Settings.Changed', expect.any(Function))
    expect(mockClient.stop).toHaveBeenCalledOnce()
  })

  it('mirrors every broadcast into lastSettingsChange for concurrent-edit awareness', async () => {
    lastSettingsChange.value = null
    const rt = useSettingsRealtime({ getToken: () => '', onChanged: () => {} })
    await rt.start()

    const handler = mockClient.on.mock.calls[0][1] as (raw: unknown) => void
    handler({ key: 'Demo:X', isRemoval: true })

    expect(lastSettingsChange.value).toMatchObject({ key: 'Demo:X', isRemoval: true })
    expect(lastSettingsChange.value!.at).toBeGreaterThan(0)
  })
})
