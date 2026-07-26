import { describe, it, expect } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { darkTheme, lightTheme, type GlobalTheme, type GlobalThemeOverrides } from 'naive-ui'
import { createThemeContext, mergeThemeSettings, THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import { useOverlayTheme, useOverlayThemeOverrides } from '../../src/headless/useOverlayTheme'

function probe(provideCtx?: ThemeContext) {
  let theme: GlobalTheme | null = null
  let overrides: GlobalThemeOverrides | null = null
  const Probe = defineComponent({
    setup() {
      theme = useOverlayTheme().value
      overrides = useOverlayThemeOverrides().value
      return () => h('div')
    },
  })
  mount(Probe, {
    global: provideCtx ? { provide: { [THEME_CONTEXT_KEY as symbol]: provideCtx } } : undefined,
  })
  return { theme: theme as GlobalTheme | null, overrides: overrides as GlobalThemeOverrides | null }
}

function ctxWithMode(mode: 'light' | 'dark'): ThemeContext {
  return createThemeContext(mergeThemeSettings({ mode }))
}

describe('useOverlayTheme', () => {
  it('resolves the dark base under global dark mode', () => {
    expect(probe(ctxWithMode('dark')).theme).toBe(darkTheme)
  })

  it('resolves the light base (null) under global light mode', () => {
    expect(probe(ctxWithMode('light')).theme).toBeNull()
  })

  it('falls back to the light base when no theme context is provided', () => {
    expect(probe().theme).toBeNull()
  })
})

describe('useOverlayThemeOverrides', () => {
  it('pins the content-area Card/DataTable repaint keys back to the light defaults', () => {
    const { overrides } = probe(ctxWithMode('light'))
    // Exactly the keys TAdminContent.innerOverrides can leak through the
    // ConfigProvider inheritance - reset to naive's own light values.
    expect(overrides?.Card?.color).toBe(lightTheme.common.cardColor)
    expect(overrides?.Card?.colorEmbedded).toBe(lightTheme.common.actionColor)
    expect(overrides?.Card?.textColor).toBe(lightTheme.common.textColor2)
    expect(overrides?.Card?.titleTextColor).toBe(lightTheme.common.textColor1)
    expect(overrides?.DataTable?.tdColor).toBe(lightTheme.common.cardColor)
    expect(overrides?.DataTable?.thColor).toBe(lightTheme.common.tableHeaderColor)
  })

  it('pins them to the dark defaults under global dark mode', () => {
    const { overrides } = probe(ctxWithMode('dark'))
    expect(overrides?.Card?.color).toBe(darkTheme.common.cardColor)
    expect(overrides?.DataTable?.tdColor).toBe(darkTheme.common.cardColor)
    expect(overrides?.DataTable?.thTextColor).toBe(darkTheme.common.textColor1)
  })
})
