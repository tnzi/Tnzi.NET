import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useModuleAvailability } from '../../src/headless/useModuleAvailability'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'

describe('useModuleAvailability', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('has() fails open while the signal is unavailable (null)', () => {
    const { has, known } = useModuleAvailability()
    expect(has('chat')).toBe(true)
    expect(known.value).toBe(false)
  })

  it('has() gates strictly once the signal is known', () => {
    useAdminRouteStore().setAvailableModules(new Set(['identity', 'chat']))
    const { has, known } = useModuleAvailability()
    expect(has('chat')).toBe(true)
    expect(has('finance')).toBe(false)
    expect(known.value).toBe(true)
  })

  it('normalizes module names like the route-level gate', () => {
    useAdminRouteStore().setAvailableModules(new Set(['ai-skills']))
    const { has } = useModuleAvailability()
    expect(has('AI.Skills')).toBe(true)
    expect(has('ai.skills')).toBe(true)
    expect(has('ai-skills')).toBe(true)
  })

  it('hasAny / hasAll compose correctly (and fail open on null signal)', () => {
    const avail = useModuleAvailability()
    expect(avail.hasAny(['chat', 'finance'])).toBe(true)
    expect(avail.hasAll(['chat', 'finance'])).toBe(true)

    useAdminRouteStore().setAvailableModules(new Set(['chat']))
    expect(avail.hasAny(['chat', 'finance'])).toBe(true)
    expect(avail.hasAny(['finance', 'payment'])).toBe(false)
    expect(avail.hasAll(['chat'])).toBe(true)
    expect(avail.hasAll(['chat', 'finance'])).toBe(false)
  })

  it('canActivate defers while the probe is pending, even with a null signal', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setModuleSignalPending(true)
    const { canActivate, pending } = useModuleAvailability()
    expect(pending.value).toBe(true)
    expect(canActivate('chat')).toBe(false)

    // Probe settles without a signal (old backend) → fail-open.
    routeStore.setModuleSignalPending(false)
    expect(canActivate('chat')).toBe(true)

    // Probe settles WITH a signal → strict gating.
    routeStore.setAvailableModules(new Set(['identity']))
    expect(canActivate('chat')).toBe(false)
    expect(canActivate('identity')).toBe(true)
  })

  it('clearRoutes resets both the signal and the pending flag', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setAvailableModules(new Set(['identity']))
    routeStore.setModuleSignalPending(true)
    routeStore.clearRoutes()
    const { has, pending, known, modules } = useModuleAvailability()
    expect(has('chat')).toBe(true) // back to fail-open
    expect(pending.value).toBe(false)
    expect(known.value).toBe(false)
    expect(modules.value).toBeNull()
  })
})
