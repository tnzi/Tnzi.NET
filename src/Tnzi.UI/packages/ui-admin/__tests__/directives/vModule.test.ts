import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { vModule } from '../../src/directives/vModule'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'

// Drive the directive hooks programmatically (mirrors vPermission.test.ts).
function probe(value: string | string[], modifier?: 'any' | 'hide'): HTMLElement {
  const el = document.createElement('div')
  el.textContent = 'x'
  document.body.appendChild(el)
  vModule.mounted!(el, {
    value,
    modifiers: modifier ? { [modifier]: true } : {},
  } as never, null as never, null as never)
  return el
}

describe('vModule directive', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
  })

  it('fails open when the module signal is unavailable (null)', () => {
    const el = probe('chat')
    expect(el.style.display).toBe('')
  })

  it('shows element when the module is loaded', () => {
    useAdminRouteStore().setAvailableModules(new Set(['chat', 'identity']))
    const el = probe('chat')
    expect(el.style.display).toBe('')
  })

  it('hides element when the module is NOT loaded', () => {
    useAdminRouteStore().setAvailableModules(new Set(['identity']))
    const el = probe('chat')
    expect(el.style.display).toBe('none')
  })

  it('normalizes module names (dots → dashes, case-insensitive)', () => {
    useAdminRouteStore().setAvailableModules(new Set(['ai-skills']))
    const el = probe('AI.Skills')
    expect(el.style.display).toBe('')
  })

  it('has NO super-user bypass - gating is about the backend, not the user', () => {
    // No auth store seeding at all: even a super admin session hides the
    // element when the backend didn't load the module.
    useAdminRouteStore().setAvailableModules(new Set([]))
    const el = probe('chat')
    expect(el.style.display).toBe('none')
  })

  it('uses visibility:hidden with the .hide modifier', () => {
    useAdminRouteStore().setAvailableModules(new Set(['identity']))
    const el = probe('chat', 'hide')
    expect(el.style.visibility).toBe('hidden')
  })

  it('array value requires ALL modules by default', () => {
    useAdminRouteStore().setAvailableModules(new Set(['chat']))
    const el = probe(['chat', 'notification'])
    expect(el.style.display).toBe('none')
  })

  it('.any modifier requires only ONE module', () => {
    useAdminRouteStore().setAvailableModules(new Set(['chat']))
    const el = probe(['chat', 'notification'], 'any')
    expect(el.style.display).toBe('')
  })

  it('reacts to the module signal arriving AFTER mount (no re-render)', async () => {
    const routeStore = useAdminRouteStore()
    const el = probe('chat')
    // Signal unknown at mount → fail-open, visible.
    expect(el.style.display).toBe('')

    routeStore.setAvailableModules(new Set(['identity']))
    await Promise.resolve() // flush the watchEffect
    expect(el.style.display).toBe('none')

    routeStore.setAvailableModules(new Set(['identity', 'chat']))
    await Promise.resolve()
    expect(el.style.display).toBe('')
  })
})
