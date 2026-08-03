import { ref } from 'vue'
import { createSettingsRealtimeClient, type SettingsChangedPayload } from '@tnzi/core/services/system'

export interface SettingsChangeSignal {
  key: string
  isRemoval: boolean
  at: number
}

/**
 * Module-level mirror of the latest `Settings.Changed` broadcast. The Settings
 * Center page watches it for concurrent-edit awareness (another session saved
 * a group this page is showing) without owning its own hub connection - the
 * shell's single subscription feeds it.
 */
export const lastSettingsChange = ref<SettingsChangeSignal | null>(null)

export interface UseSettingsRealtimeOptions {
  /**
   * Hub URL. A getter is evaluated at `start()` time, so a URL that only
   * becomes known after the backend shell signal lands can still be used.
   * Falls back to '/hubs/settings'.
   */
  hubUrl?: string | (() => string | undefined)
  /** Returns the freshest JWT (read on each (re)connect). */
  getToken: () => string
  /**
   * Called for every Global-scope setting change. The consumer routes by
   * `payload.key` prefix (e.g. `Chat:*` → re-fetch chat config,
   * `Appearance:AdminTheme` → reload the global theme).
   */
  onChanged: (payload: SettingsChangedPayload) => void
}

/**
 * Subscribes to the `/hubs/settings` realtime channel so an already-open admin
 * session live-refreshes deployment config the super admin changes - no manual
 * page reload. Mirrors {@link useChatRealtime}: the backend broadcasts only the
 * changed key, the client decides what to re-fetch.
 */
export function useSettingsRealtime(opts: UseSettingsRealtimeOptions) {
  // Built on first start(), not at setup: the URL may come from the backend
  // shell signal, which lands after this composable is created. Building eagerly
  // would freeze whatever placeholder was known at setup time.
  let signal: ReturnType<typeof createSettingsRealtimeClient> | null = null

  function resolveUrl(): string {
    const configured = typeof opts.hubUrl === 'function' ? opts.hubUrl() : opts.hubUrl
    return configured ?? '/hubs/settings'
  }

  const onChanged = (raw: unknown) => {
    const payload = raw as SettingsChangedPayload
    lastSettingsChange.value = { key: payload.key, isRemoval: !!payload.isRemoval, at: Date.now() }
    opts.onChanged(payload)
  }

  async function start() {
    signal ??= createSettingsRealtimeClient({
      url: resolveUrl(),
      accessTokenFactory: () => opts.getToken(),
    })
    signal.on('Settings.Changed', onChanged)
    await signal.start()
  }
  async function stop() {
    if (!signal) return
    signal.off('Settings.Changed', onChanged)
    await signal.stop()
  }
  /** The underlying hub client, or `null` until the first `start()`. */
  function client() {
    return signal
  }
  return { start, stop, client }
}
