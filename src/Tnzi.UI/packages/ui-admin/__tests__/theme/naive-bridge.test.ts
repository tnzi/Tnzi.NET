import { describe, it, expect } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import type { GlobalThemeOverrides } from 'naive-ui'
import { CONTRAST_TEXT_DARK, defaultThemeSettings, mergeThemeSettings } from '@tnzi/ui/theme'
import { createThemeContext, type ThemeContext } from '@tnzi/ui/headless'
import {
  ADMIN_NAIVE_OVERRIDES_KEY,
  buildNaiveThemeOverrides,
  resolveNaiveOverrides,
  useNaiveThemeOverrides,
  type NaiveOverridesSource,
} from '../../src/theme/naive-bridge'

describe('buildNaiveThemeOverrides', () => {
  it('generates a common section with primary color', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings)
    expect(overrides.common?.primaryColor).toBe('#0d9488')
  })

  it('generates hover/pressed/suppl variants from palette', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings)
    expect(overrides.common?.primaryColorHover).toBeDefined()
    expect(overrides.common?.primaryColorPressed).toBeDefined()
    expect(overrides.common?.primaryColorSuppl).toBeDefined()
    // Hover should be lighter than pressed
    expect(overrides.common?.primaryColorHover).not.toBe(overrides.common?.primaryColorPressed)
  })

  it('applies all 5 semantic color roles', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings)
    expect(overrides.common?.primaryColor).toBe('#0d9488')
    expect(overrides.common?.infoColor).toBe('#2080f0')
    expect(overrides.common?.successColor).toBe('#18a058')
    expect(overrides.common?.warningColor).toBe('#f0a020')
    expect(overrides.common?.errorColor).toBe('#d03050')
  })

  it('merges the consumer overrides', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings, { Menu: { itemHeight: '50px' } })
    expect((overrides as any).Menu?.itemHeight).toBe('50px')
  })

  it('consumer common keys override generated base', () => {
    const overrides = buildNaiveThemeOverrides(defaultThemeSettings, {
      common: { primaryColorHover: '#ff00ff' },
    })
    expect(overrides.common?.primaryColorHover).toBe('#ff00ff')
    // Non-overridden base keys must survive the merge
    expect(overrides.common?.primaryColor).toBe('#0d9488')
    expect(overrides.common?.errorColor).toBe('#d03050')
  })

  it('is pure - does not mutate the consumer overrides', () => {
    const naiveOverrides = { common: { primaryColorHover: '#000000' } }
    const snapshot = JSON.stringify(naiveOverrides)
    buildNaiveThemeOverrides(defaultThemeSettings, naiveOverrides)
    expect(JSON.stringify(naiveOverrides)).toBe(snapshot)
  })

  describe('foreground on a primary fill', () => {
    const withPrimary = (primary: string, naiveOverrides?: GlobalThemeOverrides) =>
      buildNaiveThemeOverrides(mergeThemeSettings({ colors: { primary } }), naiveOverrides)

    it('paints the ink on every primary fill naive draws when the primary is light', () => {
      const overrides = withPrimary('#FFD217')
      expect(overrides.Button).toMatchObject({
        textColorPrimary: CONTRAST_TEXT_DARK,
        textColorHoverPrimary: CONTRAST_TEXT_DARK,
        textColorPressedPrimary: CONTRAST_TEXT_DARK,
        textColorFocusPrimary: CONTRAST_TEXT_DARK,
        textColorDisabledPrimary: CONTRAST_TEXT_DARK,
      })
      expect(overrides.Checkbox?.checkMarkColor).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Tag?.textColorChecked).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Calendar?.dateTextColorCurrent).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.DatePicker?.itemTextColorActive).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Steps?.indicatorTextColorProcess).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.FloatButton?.textColorPrimary).toBe(CONTRAST_TEXT_DARK)
    })

    // naive's own choice (white in light mode, black in dark mode) stays for
    // every primary white reads on, the default teal included.
    it.each(['#0d9488', '#2563eb'])('leaves naive alone when white reads on the primary (%s)', (primary) => {
      const overrides = withPrimary(primary)
      expect(Object.keys(overrides)).toEqual(['common'])
    })

    it('lets a consumer key win inside a generated section and keeps the rest', () => {
      const overrides = withPrimary('#FFD217', {
        Button: { textColorPrimary: '#000000', heightMedium: '40px' },
        Menu: { itemHeight: '50px' },
      })
      expect(overrides.Button?.textColorPrimary).toBe('#000000')
      expect(overrides.Button?.heightMedium).toBe('40px')
      expect(overrides.Button?.textColorHoverPrimary).toBe(CONTRAST_TEXT_DARK)
      expect(overrides.Menu?.itemHeight).toBe('50px')
    })

    it('passes a consumer section through unchanged when the primary is dark', () => {
      const overrides = withPrimary('#2563eb', { Button: { heightMedium: '40px' } })
      expect(overrides.Button).toEqual({ heightMedium: '40px' })
    })
  })
})

describe('useNaiveThemeOverrides', () => {
  function overridesIn(ctx: ThemeContext | null, provided?: NaiveOverridesSource) {
    let result: ReturnType<typeof useNaiveThemeOverrides> | undefined
    const Probe = defineComponent({
      setup() {
        result = useNaiveThemeOverrides(ctx)
        return () => h('div')
      },
    })
    mount(Probe, {
      global: { provide: provided ? { [ADMIN_NAIVE_OVERRIDES_KEY as symbol]: provided } : {} },
    })
    return result!
  }

  // A brand colour picked at runtime (an account preference) must carry its
  // readable foreground into naive without a reload.
  it('follows the theme context when the primary changes', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const overrides = overridesIn(ctx)
    expect(overrides.value.Button).toBeUndefined()
    ctx.setColor('primary', '#FFD217')
    await nextTick()
    expect(overrides.value.Button?.textColorPrimary).toBe(CONTRAST_TEXT_DARK)
    ctx.setColor('primary', '#2563EB')
    await nextTick()
    expect(overrides.value.Button).toBeUndefined()
  })

  it('merges the overrides given to createTnziUiAdmin and keeps them across a colour change', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const overrides = overridesIn(ctx, { common: { borderRadius: '10px' } })
    expect(overrides.value.common?.borderRadius).toBe('10px')
    ctx.setColor('primary', '#ff0000')
    await nextTick()
    expect(overrides.value.common?.borderRadius).toBe('10px')
    expect(overrides.value.common?.primaryColor).toBe('#ff0000')
  })

  it('without a theme context gives the raw overrides only', () => {
    expect(overridesIn(null).value).toEqual({})
    expect(overridesIn(null, { Menu: { itemHeight: '50px' } }).value).toEqual({ Menu: { itemHeight: '50px' } })
  })

  // One set of overrides for each mode, configured once: switching the mode
  // must swap the set without the application passing anything to the root.
  const LIGHT: GlobalThemeOverrides = { common: { bodyColor: '#ffffff' }, Card: { color: '#fafafa' } }
  const DARK: GlobalThemeOverrides = { common: { bodyColor: '#141414' }, Card: { color: '#1c1c1b' } }

  it('calls a function source with the current mode and follows a switch', async () => {
    const ctx = createThemeContext({ ...defaultThemeSettings, mode: 'light' })
    const overrides = overridesIn(ctx, ({ isDark }) => (isDark ? DARK : LIGHT))
    expect(overrides.value.Card?.color).toBe('#fafafa')
    expect(overrides.value.common?.bodyColor).toBe('#ffffff')
    ctx.setMode('dark')
    await nextTick()
    expect(overrides.value.Card?.color).toBe('#1c1c1b')
    expect(overrides.value.common?.bodyColor).toBe('#141414')
    // The palette tokens are still merged under the consumer's set.
    expect(overrides.value.common?.primaryColor).toBe(ctx.settings.value.palettes.primary[500])
    ctx.toggleTheme()
    await nextTick()
    expect(overrides.value.Card?.color).toBe('#fafafa')
  })

  it('passes the settings to a function source and follows a colour change', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const overrides = overridesIn(ctx, ({ settings }) => ({ Menu: { itemColorActive: settings?.colors.primary ?? '' } }))
    ctx.setColor('primary', '#2563eb')
    await nextTick()
    expect(overrides.value.Menu?.itemColorActive).toBe('#2563eb')
  })

  it('follows a ref source', async () => {
    const ctx = createThemeContext(defaultThemeSettings)
    const source = ref<GlobalThemeOverrides>(LIGHT)
    const overrides = overridesIn(ctx, source)
    expect(overrides.value.Card?.color).toBe('#fafafa')
    source.value = DARK
    await nextTick()
    expect(overrides.value.Card?.color).toBe('#1c1c1b')
  })

  it('without a theme context calls a function source as the light mode', () => {
    expect(overridesIn(null, ({ isDark }) => (isDark ? DARK : LIGHT)).value).toEqual(LIGHT)
  })
})

describe('resolveNaiveOverrides', () => {
  const light = { isDark: false, settings: null }
  it('gives an empty set for no source and for a function that returns nothing', () => {
    expect(resolveNaiveOverrides(undefined, light)).toEqual({})
    expect(resolveNaiveOverrides((() => undefined) as unknown as NaiveOverridesSource, light)).toEqual({})
  })
  it('returns an object source as it is', () => {
    const source = { Menu: { itemHeight: '40px' } }
    expect(resolveNaiveOverrides(source, { isDark: true, settings: null })).toBe(source)
  })
})
