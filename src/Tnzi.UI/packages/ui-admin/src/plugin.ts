import type { App } from 'vue'

// Phase 2a: legacy Tailwind-era component registrations removed. The new
// createTnziUiAdmin() plugin (replacing this shim) arrives in Phase 2b and
// will register the T-prefixed Naive UI component family.
export interface TnziAdminOptions {
  registerComponents?: boolean
}

export function createTnziAdmin(_options: TnziAdminOptions = {}) {
  return {
    install(_app: App) {
      // No-op during the Phase 2a stores-foundation stage.
    },
  }
}
