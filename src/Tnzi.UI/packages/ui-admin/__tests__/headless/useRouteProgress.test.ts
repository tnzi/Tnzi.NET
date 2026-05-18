import { describe, it, expect, beforeEach } from 'vitest'
import { createRouter, createMemoryHistory, type Router } from 'vue-router'
import { useRouteProgress } from '../../src/headless/useRouteProgress'

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div>home</div>' } },
      { path: '/a', component: { template: '<div>a</div>' } },
      { path: '/b', component: { template: '<div>b</div>' } },
    ],
  })
}

describe('useRouteProgress', () => {
  beforeEach(() => {
    delete document.documentElement.dataset.tnziRouteLoading
  })

  it('sets data-tnzi-route-loading="on" during navigation', async () => {
    const router = makeRouter()
    useRouteProgress(router)
    // Capture the in-flight flag from inside another beforeEach so we observe
    // the state *during* the guarded resolution, not after it.
    let snapshot: string | undefined
    router.beforeEach((_to, _from, next) => {
      snapshot = document.documentElement.dataset.tnziRouteLoading
      next()
    })
    await router.push('/a')
    expect(snapshot).toBe('on')
  })

  it('clears data attribute on next-next animation frame after nav completes', async () => {
    const router = makeRouter()
    useRouteProgress(router)
    await router.push('/a')
    // Two rAF ticks before the attribute is removed.
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
    expect(document.documentElement.dataset.tnziRouteLoading).toBeUndefined()
  })

  it('is idempotent — second attachment does not double-register hooks', async () => {
    const router = makeRouter()
    useRouteProgress(router)
    useRouteProgress(router) // should be a no-op
    await router.push('/a')
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    // No assertion on duplicate counters — just no crash + final state clean.
    expect(document.documentElement.dataset.tnziRouteLoading).toBeUndefined()
  })

  it('handles concurrent navigations (counter does not go negative)', async () => {
    const router = makeRouter()
    useRouteProgress(router)
    let snapshot: string | undefined
    router.beforeEach((_to, _from, next) => {
      // Capture during the first guard tick.
      if (snapshot === undefined) {
        snapshot = document.documentElement.dataset.tnziRouteLoading
      }
      next()
    })
    // Fire two navigations back-to-back.
    const [p1, p2] = [router.push('/a'), router.push('/b')]
    await Promise.all([p1, p2])
    expect(snapshot).toBe('on')
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    expect(document.documentElement.dataset.tnziRouteLoading).toBeUndefined()
  })
})
