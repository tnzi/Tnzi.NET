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

  it('is idempotent - second attachment does not double-register hooks', async () => {
    const router = makeRouter()
    useRouteProgress(router)
    useRouteProgress(router) // should be a no-op
    await router.push('/a')
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    // No assertion on duplicate counters - just no crash + final state clean.
    expect(document.documentElement.dataset.tnziRouteLoading).toBeUndefined()
  })

  it('clears the attribute after a guard redirect (regression: bar stuck at 80%)', async () => {
    const router = makeRouter()
    useRouteProgress(router)
    // A redirecting guard registered AFTER progress: visiting /a bounces to /b,
    // mirroring the auth guard sending an unauthenticated first visit to /login
    // (or `/` → /dashboard). progress.beforeEach fires for the aborted /a nav
    // (seq++/on) with no matching landing afterEach - the old pending counter
    // leaked here and never returned to 0, so the bar stuck at ~80% forever.
    router.beforeEach((to, _from, next) => {
      if (to.path === '/a') return next('/b')
      next()
    })
    await router.push('/a')
    expect(router.currentRoute.value.path).toBe('/b')
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    await new Promise<void>((r) => requestAnimationFrame(() => r()))
    expect(document.documentElement.dataset.tnziRouteLoading).toBeUndefined()
  })

  it('handles concurrent navigations (bar clears once the latest settles)', async () => {
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
