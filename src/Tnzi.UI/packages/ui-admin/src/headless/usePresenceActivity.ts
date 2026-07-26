/**
 * Presence auto-away wiring for the admin shell.
 *
 * Loads the presence client config (auto-away threshold / toggles) and drives the
 * framework-agnostic activity reporter from `@tnzi/core/services/presence`. The reporter
 * only fires on idle transitions (no heartbeat), and stays inert when presence or auto-away
 * is disabled (via `isEnabled`). Mirrors the settings-realtime / global-theme start/stop
 * wiring in AdminShellRoot.
 */
import { ref } from 'vue'
import type { HttpClient } from '@tnzi/core/http'
import {
  usePresenceApi,
  createPresenceActivityReporter,
  type PresenceActivityReporter,
  type PresenceClientConfigDto,
} from '@tnzi/core/services/presence'

// Start disabled and only enable once a real config load succeeds. `Tnzi.Identity.Presence`
// is an OPTIONAL module - an app may load Identity/auth without it. If it's absent,
// `GET /presence/config` 404s and the reporter must stay INERT (never fire a doomed
// POST /presence/activity). So on any load failure/empty we keep the disabled config.
const DISABLED_PRESENCE_CONFIG: PresenceClientConfigDto = {
  enablePresence: false,
  allowInvisible: true,
  autoAwayEnabled: false,
  autoAwayMinutes: 15,
}

export function usePresenceActivity(client: HttpClient | null | undefined) {
  const config = ref<PresenceClientConfigDto>({ ...DISABLED_PRESENCE_CONFIG })
  let reporter: PresenceActivityReporter | null = null

  async function loadConfig(): Promise<void> {
    if (!client) return
    try {
      const res = await usePresenceApi(client).getConfig()
      const data = (res as { data?: PresenceClientConfigDto } | null)?.data
      // Presence loaded → adopt real config; empty payload → stay disabled.
      config.value = data && typeof data === 'object' ? data : { ...DISABLED_PRESENCE_CONFIG }
    } catch {
      // Presence module not loaded (404) / probe failed → stay disabled so isEnabled() is
      // false and the reporter never sends a doomed activity POST.
      config.value = { ...DISABLED_PRESENCE_CONFIG }
    }
  }

  function start(): void {
    if (!client || reporter) return
    reporter = createPresenceActivityReporter({
      client,
      getIdleMinutes: () => config.value.autoAwayMinutes,
      isEnabled: () => config.value.enablePresence && config.value.autoAwayEnabled,
    })
    reporter.start()
  }

  function stop(): void {
    reporter?.stop()
    reporter = null
  }

  return { config, loadConfig, start, stop }
}
