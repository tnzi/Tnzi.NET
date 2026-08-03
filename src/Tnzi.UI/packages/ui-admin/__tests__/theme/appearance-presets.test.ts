import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { ThemeContext } from '@tnzi/ui'
import {
  BUILTIN_APPEARANCE_PRESETS,
  applyAppearancePreset,
} from '../../src/theme/appearance-presets'
import { parseColor } from '../../src/theme/surfaceTone'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'

/** Minimal ThemeContext stub - applyAppearancePreset only needs setColor/setMode. */
function fakeCtx(): ThemeContext {
  return {
    setColor: vi.fn(),
    setMode: vi.fn(),
  } as unknown as ThemeContext
}

describe('appearancePresets', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('ships a curated set of looks, each with a name + primary', () => {
    expect(BUILTIN_APPEARANCE_PRESETS.length).toBeGreaterThanOrEqual(6)
    for (const p of BUILTIN_APPEARANCE_PRESETS) {
      expect(p.name).toBeTruthy()
      expect(p.primary).toMatch(/^#[0-9a-fA-F]{6}$/)
    }
  })

  it('look names are unique and every surface color parses (adaptive tone depends on it)', () => {
    const names = BUILTIN_APPEARANCE_PRESETS.map((p) => p.name)
    expect(new Set(names).size).toBe(names.length)
    const surfaceKeys = ['siderBg', 'headerBg', 'tabBg', 'footerBg', 'contentBg', 'pageHeaderBg', 'cardBg'] as const
    for (const p of BUILTIN_APPEARANCE_PRESETS) {
      for (const key of surfaceKeys) {
        const v = p[key]
        if (v != null) {
          expect(parseColor(v), `${p.name}.${key} = ${v}`).not.toBeNull()
        }
      }
    }
  })

  it('every built-in look has an en + zh label (the drawer derives labels from i18n)', () => {
    const enLooks = en.admin.theme.preset.looks as Record<string, string>
    const zhLooks = (zhCn as typeof en).admin.theme.preset.looks as Record<string, string>
    for (const p of BUILTIN_APPEARANCE_PRESETS) {
      expect(enLooks[p.name], `en label for ${p.name}`).toBeTruthy()
      expect(zhLooks[p.name], `zh label for ${p.name}`).toBeTruthy()
    }
  })

  it('applies accent + mode + inverted-sider shorthand', () => {
    const store = useAdminThemeStore()
    const ctx = fakeCtx()
    store.invertSider = false
    applyAppearancePreset(
      { name: 'x', primary: '#7C3AED', mode: 'dark', invertSider: true },
      store,
      ctx,
    )
    expect(ctx.setColor).toHaveBeenCalledWith('primary', '#7C3AED')
    expect(ctx.setMode).toHaveBeenCalledWith('dark')
    expect(store.invertSider).toBe(true)
  })

  it('applies custom surface colors and clears the ones it does not specify', () => {
    const store = useAdminThemeStore()
    const ctx = fakeCtx()
    // Stale overrides the preset must reconcile.
    store.setSiderBg('#eeeeee')
    store.setContentBg('#dddddd')
    store.setPageHeaderBg('#cccccc')
    store.setCardBg('#bbbbbb')
    applyAppearancePreset(
      { name: 'midnight', primary: '#6366F1', siderBg: '#0F172A', headerBg: '#0F172A', cardBg: '#1E293B' },
      store,
      ctx,
    )
    expect(store.siderBg).toBe('#0F172A')
    expect(store.headerBg).toBe('#0F172A')
    expect(store.cardBg).toBe('#1E293B')
    // Unspecified surfaces are cleared so the look is coherent.
    expect(store.tabBg).toBeNull()
    expect(store.footerBg).toBeNull()
    expect(store.contentBg).toBeNull()
    expect(store.pageHeaderBg).toBeNull()
  })

  it('leaves layout / radius / tab style untouched when the preset omits them', () => {
    const store = useAdminThemeStore()
    const ctx = fakeCtx()
    store.setLayoutMode('horizontal')
    store.setThemeRadius(10)
    applyAppearancePreset({ name: 'color-only', primary: '#10B981' }, store, ctx)
    expect(store.layoutMode).toBe('horizontal')
    expect(store.themeRadius).toBe(10)
  })
})
