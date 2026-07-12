import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui'
import { useGlobalTheme } from '../../src/headless/useGlobalTheme'
import { buildThemeSnapshot } from '../../src/theme/snapshot'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'
import type { AdminGlobalThemeDto } from '@tnzi/core/services/system'

function createCtx(): ThemeContext {
  return createThemeContext(
    mergeThemeSettings({
      colors: { primary: '#3b82f6' },
      mode: 'light',
    }),
  )
}

function fakeBridge(theme: Record<string, unknown> | null) {
  return {
    appearance: {
      getGlobal: vi.fn(async (): Promise<AdminGlobalThemeDto | null> => ({ theme, updatedAt: null })),
      saveGlobal: vi.fn(async (t: Record<string, unknown>) => ({ theme: t, updatedAt: null })),
      resetGlobal: vi.fn(async () => undefined),
    },
  }
}

describe('useGlobalTheme', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('load() applies a valid server snapshot to the store + context', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    // Serialize a "remote" theme authored elsewhere.
    const authorStore = useAdminThemeStore()
    authorStore.setLayoutMode('horizontal')
    authorStore.setTabVisible(false)
    const remote = buildThemeSnapshot(authorStore, createCtx())
    authorStore.reset()

    const controller = useGlobalTheme({
      themeContext: ctx,
      bridge: fakeBridge(remote as unknown as Record<string, unknown>),
    })
    await controller.load()

    expect(controller.loaded.value).toBe(true)
    expect(controller.remote.value).not.toBeNull()
    expect(store.layoutMode).toBe('horizontal')
    expect(store.tabVisible).toBe(false)
  })

  it('load() re-overlays the user preset color on top of the global colors', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    store.setUserPresetColor('#EF4444')
    const remote = buildThemeSnapshot(store, createCtx())
    ;(remote.ui.colors as Record<string, string>).primary = '#10B981'

    const controller = useGlobalTheme({
      themeContext: ctx,
      bridge: fakeBridge(remote as unknown as Record<string, unknown>),
    })
    await controller.load()
    expect(ctx.settings.value.colors.primary).toBe('#EF4444')
  })

  it('load() ignores an invalid / unset payload (legacy backend degrades gracefully)', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    store.setLayoutMode('vertical-mix')

    const controller = useGlobalTheme({
      themeContext: ctx,
      bridge: fakeBridge(null),
    })
    await controller.load()
    expect(controller.remote.value).toBeNull()
    expect(store.layoutMode).toBe('vertical-mix')
    // Nothing saved yet → everything counts as unsaved (first-time
    // configuration gets a dirty signal).
    expect(controller.isDirty.value).toBe(true)
  })

  it('shouldOverlayUserPreset=false keeps the global colors pure (privileged editor)', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    store.setUserPresetColor('#EF4444')
    const remote = buildThemeSnapshot(store, createCtx())
    ;(remote.ui.colors as Record<string, string>).primary = '#10B981'

    const controller = useGlobalTheme({
      themeContext: ctx,
      bridge: fakeBridge(remote as unknown as Record<string, unknown>),
      shouldOverlayUserPreset: () => false,
    })
    await controller.load()
    // The lingering personal color must NOT overlay - a subsequent save()
    // would otherwise publish it globally.
    expect(ctx.settings.value.colors.primary).toBe('#10B981')
  })

  it('save() persists the current state and refreshes `remote` (isDirty settles false)', async () => {
    const ctx = createCtx()
    const store = useAdminThemeStore()
    const bridge = fakeBridge(null)
    const controller = useGlobalTheme({ themeContext: ctx, bridge })

    store.setLayoutMode('horizontal')
    const ok = await controller.save()
    expect(ok).toBe(true)
    expect(bridge.appearance.saveGlobal).toHaveBeenCalled()
    expect(controller.remote.value?.admin.layoutMode).toBe('horizontal')
    expect(controller.isDirty.value).toBe(false)

    store.setTabVisible(false)
    expect(controller.isDirty.value).toBe(true)
  })

  it('save() resolves false when the backend rejects (403 never reads as saved)', async () => {
    const bridge = fakeBridge(null)
    bridge.appearance.saveGlobal.mockRejectedValueOnce(new Error('forbidden'))
    const controller = useGlobalTheme({ themeContext: createCtx(), bridge })
    expect(await controller.save()).toBe(false)
    expect(controller.remote.value).toBeNull()
  })

  it('reset() clears the server snapshot', async () => {
    const bridge = fakeBridge(null)
    const controller = useGlobalTheme({ themeContext: createCtx(), bridge })
    await controller.save()
    expect(controller.remote.value).not.toBeNull()
    expect(await controller.reset()).toBe(true)
    expect(controller.remote.value).toBeNull()
    expect(bridge.appearance.resetGlobal).toHaveBeenCalled()
  })

  it('is disabled without a client/bridge or theme context', async () => {
    const controller = useGlobalTheme({ themeContext: createCtx() })
    expect(controller.enabled).toBe(false)
    await controller.load()
    expect(controller.loaded.value).toBe(true)
    expect(await controller.save()).toBe(false)
  })
})
