import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { defaultAdminRoutes } from '../../src/router/routes'
import { useAdminRouteStore, type AdminRouteRecord } from '../../src/stores/useAdminRouteStore'

/**
 * A page whose endpoints live in an optional sub-module carries that module's
 * gate, so a host that never loaded it hides the page instead of letting it
 * render the 404 its reads now report. Permission seeding alone is not a gate:
 * a super-admin holds every code, declared or not.
 */
describe('routes of optional sub-modules', () => {
  beforeEach(() => setActivePinia(createPinia()))

  function storeWith(modules: string[]) {
    const store = useAdminRouteStore()
    store.setAuthRoutes(defaultAdminRoutes as unknown as AdminRouteRecord[])
    store.setAvailableModules(new Set(modules))
    return store
  }

  it('push devices are unreachable on a host without Tnzi.Notification.Push', () => {
    const store = storeWith(['notification'])
    expect(store.unavailableRouteNames.has('notification')).toBe(false)
    expect(store.unavailableRouteNames.has('notification.devices')).toBe(true)
  })

  it('push devices are reachable once the push sub-module is loaded', () => {
    const store = storeWith(['notification', 'notification-push'])
    expect(store.unavailableRouteNames.has('notification.devices')).toBe(false)
  })

  // Every workflow page reads the admin/workflows endpoints, which only
  // Tnzi.AI.Workflow serves: the list, its editor and the run viewer.
  it.each(['ai.workflows', 'ai.workflows.editor', 'ai.workflowRuns'])(
    '%s is unreachable on a host without Tnzi.AI.Workflow',
    (name) => {
      const store = storeWith(['ai'])
      expect(store.unavailableRouteNames.has('ai')).toBe(false)
      expect(store.unavailableRouteNames.has(name)).toBe(true)
    },
  )

  it.each(['ai.workflows', 'ai.workflows.editor', 'ai.workflowRuns'])(
    '%s is reachable once the workflow sub-module is loaded',
    (name) => {
      const store = storeWith(['ai', 'ai-workflow'])
      expect(store.unavailableRouteNames.has(name)).toBe(false)
    },
  )
})
