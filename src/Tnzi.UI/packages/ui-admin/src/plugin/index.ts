import type { App } from 'vue'
import type { Pinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import type { HttpClient } from '@tnzi/core/http/http'
import '../styles/index.css'
import { TNZI_ADMIN_CLIENT_KEY } from './client'

export interface TnziUiAdminOptions {
  pinia?: Pinia
  installPersistedstate?: boolean
  globalSearchShortcut?: string
  /**
   * HttpClient that admin bridges use to talk to the backend.
   * If omitted, pages that call `useAdminClient()` will throw, and
   * bridge factories stay on their stub / mock paths.
   */
  client?: HttpClient
}

export interface TnziUiAdminInstance {
  uninstall(): void
}

export function createTnziUiAdmin(app: App, options: TnziUiAdminOptions = {}): TnziUiAdminInstance {
  if (options.installPersistedstate !== false && options.pinia) {
    options.pinia.use(piniaPluginPersistedstate)
  }

  if (options.client) {
    app.provide(TNZI_ADMIN_CLIENT_KEY, options.client)
  }

  let keyHandler: ((e: KeyboardEvent) => void) | null = null
  if (typeof window !== 'undefined') {
    keyHandler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        window.dispatchEvent(new CustomEvent('tnzi:global-search-toggle'))
      }
    }
    window.addEventListener('keydown', keyHandler)
  }

  return {
    uninstall() {
      if (keyHandler) window.removeEventListener('keydown', keyHandler)
    },
  }
}

export { TnziUiAdminResolver } from './resolver'
export { TNZI_ADMIN_CLIENT_KEY, useAdminClient } from './client'
