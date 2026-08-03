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
import type { AdminSettingsConfig } from '../../src/plugin/settings-config'
import { createSettingsRealtimeClient } from '@tnzi/core/services/system'

describe('useSettingsRealtime', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // The client is built on the first `start()`, not at setup: the hub URL can
  // come from the backend shell signal, which lands after this composable is
  // created. These assertions therefore start the connection first - building
  // eagerly is exactly the behaviour that was removed.
  it('connects to /hubs/settings by default and wires the token factory', async () => {
    const getToken = vi.fn(() => 'tok')
    const rt = useSettingsRealtime({ getToken, onChanged: () => {} })
    await rt.start()

    const opts = (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0]
    expect(opts.url).toBe('/hubs/settings')
    expect(opts.accessTokenFactory()).toBe('tok')
    expect(getToken).toHaveBeenCalled()
  })

  it('does not build the client until start() is called', () => {
    useSettingsRealtime({ getToken: () => '', onChanged: () => {} })
    expect(createSettingsRealtimeClient).not.toHaveBeenCalled()
  })

  it('honours a hubUrl override', async () => {
    const rt = useSettingsRealtime({
      hubUrl: '/api/hubs/settings',
      getToken: () => '',
      onChanged: () => {},
    })
    await rt.start()
    const opts = (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0]
    expect(opts.url).toBe('/api/hubs/settings')
  })

  it('resolves a hubUrl getter at start(), not at setup', async () => {
    // The point of the getter form: a URL discovered later (backend shell
    // signal) still reaches the client. Evaluating at setup would freeze the
    // placeholder that was known then.
    let discovered: string | undefined
    const rt = useSettingsRealtime({
      hubUrl: () => discovered,
      getToken: () => '',
      onChanged: () => {},
    })

    discovered = '/api/hubs/settings'
    await rt.start()

    expect(
      (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0].url,
    ).toBe('/api/hubs/settings')
  })

  it('forwards AdminSettingsConfig.hubUrl through to the client (AdminShellRoot wiring)', async () => {
    // Mirrors AdminShellRoot: `hubUrl: settingsConfig?.hubUrl`, so a config that
    // sets hubUrl (e.g. under an IIS sub-path) reaches the SignalR client, and an
    // absent config falls back to the root-relative default.
    const settingsConfig: AdminSettingsConfig = { hubUrl: '/api/hubs/settings' }
    const configured = useSettingsRealtime({
      hubUrl: settingsConfig?.hubUrl,
      getToken: () => '',
      onChanged: () => {},
    })
    await configured.start()
    expect(
      (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[0][0].url,
    ).toBe('/api/hubs/settings')

    const noHubConfig: AdminSettingsConfig | null = null
    const fallback = useSettingsRealtime({
      hubUrl: noHubConfig?.hubUrl,
      getToken: () => '',
      onChanged: () => {},
    })
    await fallback.start()
    expect(
      (createSettingsRealtimeClient as unknown as ReturnType<typeof vi.fn>).mock.calls[1][0].url,
    ).toBe('/hubs/settings')
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
