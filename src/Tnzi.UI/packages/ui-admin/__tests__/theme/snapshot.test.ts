import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui'
import { buildThemeSnapshot, applyThemeSnapshot } from '../../src/theme/snapshot'
import { isValidSnapshot } from '../../src/theme/admin-config'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'

function createCtx(mode: 'light' | 'dark' | 'auto' = 'light'): ThemeContext {
  return createThemeContext(
    mergeThemeSettings({
      colors: { primary: '#3b82f6' },
      mode,
    }),
  )
}

describe('theme snapshot build/apply', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('build → apply round-trips the admin layout + visibility state', () => {
    const store = useAdminThemeStore()
    const ctx = createCtx()
    store.setLayoutMode('horizontal')
    store.setTabVisible(false)
    store.setPresetPickerVisible(false)
    store.setThemeRadius(10)
    ctx.setColor('primary', '#10B981')

    const snapshot = buildThemeSnapshot(store, ctx)
    expect(isValidSnapshot(snapshot)).toBe(true)
    expect(snapshot.admin.tabVisible).toBe(false)
    expect(snapshot.admin.presetPickerVisible).toBe(false)
    expect(snapshot.ui.colors.primary).toBe('#10B981')

    // Apply onto a fresh store/context - the receiving side of the
    // "super admin themes everyone" flow.
    setActivePinia(createPinia())
    const store2 = useAdminThemeStore()
    const ctx2 = createCtx()
    applyThemeSnapshot(snapshot, store2, ctx2)
    expect(store2.layoutMode).toBe('horizontal')
    expect(store2.tabVisible).toBe(false)
    expect(store2.presetPickerVisible).toBe(false)
    expect(store2.themeRadius).toBe(10)
    expect(ctx2.settings.value.colors.primary).toBe('#10B981')
  })

  it('modeAsDefault: a user who DIVERGED from the previous default keeps their choice', () => {
    const store = useAdminThemeStore()
    const ctx = createCtx('dark')
    store.setLastAppliedDefaultMode('light') // previous global default
    store.setThemeSchema('dark') // the user's own divergent choice

    const snapshot = buildThemeSnapshot(store, createCtx('light'))
    applyThemeSnapshot(snapshot, store, ctx, { modeAsDefault: true })
    expect(ctx.settings.value.mode).toBe('dark')
  })

  it('modeAsDefault: a NEW default reaches users whose schema was only mirror-recorded', () => {
    const store = useAdminThemeStore()
    const ctx = createCtx('light')
    // Previous boot applied the 'light' default; the ctx→themeSchema mirror
    // recorded it - the user never actually clicked the toggle.
    store.setLastAppliedDefaultMode('light')
    store.setThemeSchema('light')

    const snapshot = buildThemeSnapshot(store, createCtx('dark'))
    applyThemeSnapshot(snapshot, store, ctx, { modeAsDefault: true })
    expect(ctx.settings.value.mode).toBe('dark')
    expect(store.lastAppliedDefaultMode).toBe('dark')
  })

  it('modeAsDefault: hiding the schema toggle locks the mode to the snapshot value', () => {
    const store = useAdminThemeStore()
    const ctx = createCtx('dark')
    store.setLastAppliedDefaultMode('light')
    store.setThemeSchema('dark')

    const source = useAdminThemeStore()
    source.setThemeSchemaVisible(false)
    const snapshot = buildThemeSnapshot(source, createCtx('light'))

    applyThemeSnapshot(snapshot, store, ctx, { modeAsDefault: true })
    expect(ctx.settings.value.mode).toBe('light')
    expect(store.themeSchemaVisible).toBe(false)
  })

  it('modeAsDefault: adopts the snapshot mode when the user never chose', () => {
    const store = useAdminThemeStore()
    const ctx = createCtx('light')
    expect(store.themeSchema).toBeNull()

    const snapshot = buildThemeSnapshot(store, createCtx('dark'))
    applyThemeSnapshot(snapshot, store, ctx, { modeAsDefault: true })
    expect(ctx.settings.value.mode).toBe('dark')
  })

  it('without modeAsDefault (drawer import) the mode applies unconditionally', () => {
    const store = useAdminThemeStore()
    const ctx = createCtx('dark')
    store.setThemeSchema('dark')

    const snapshot = buildThemeSnapshot(store, createCtx('light'))
    applyThemeSnapshot(snapshot, store, ctx)
    expect(ctx.settings.value.mode).toBe('light')
  })
})
