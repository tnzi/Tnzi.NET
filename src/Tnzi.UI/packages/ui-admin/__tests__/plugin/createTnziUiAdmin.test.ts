import { describe, it, expect } from 'vitest'
import { createApp, defineComponent, h } from 'vue'
import { THEME_CONTEXT_KEY } from '@tnzi/ui'
import {
  createTnziUiAdmin,
  DEFAULT_ADMIN_PRIMARY_COLOR,
} from '../../src/plugin'

interface ThemeCtxLike {
  settings: { value: { colors: { primary: string } } }
}

function mountAndReadCtx(plugin: ReturnType<typeof createTnziUiAdmin>): {
  ctx: ThemeCtxLike | undefined
  uninstall: () => void
} {
  void plugin // ensure plugin is referenced
  // Caller installs the plugin on its own app; we just expose the cleanup.
  return { ctx: undefined, uninstall: () => plugin.uninstall() }
}

describe('createTnziUiAdmin', () => {
  it('injects a fallback theme context with the admin default primary when no theme installed', () => {
    // Default primary updated from '#646cff' (soybean purple) → '#06B6D4'
    // (cyan-500) to match the ui-admin default palette spec.
    const app = createApp(defineComponent({ render: () => h('div') }))
    const plugin = createTnziUiAdmin(app)

    const provides = (app as unknown as {
      _context: { provides: Record<symbol, unknown> }
    })._context.provides
    const ctx = provides[THEME_CONTEXT_KEY] as ThemeCtxLike | undefined
    expect(ctx).toBeDefined()
    expect(ctx?.settings.value.colors.primary).toBe(DEFAULT_ADMIN_PRIMARY_COLOR)
    expect(DEFAULT_ADMIN_PRIMARY_COLOR).toBe('#06B6D4')

    plugin.uninstall()
  })

  it('honors themeOverride when no theme installed', () => {
    const app = createApp(defineComponent({ render: () => h('div') }))
    const plugin = createTnziUiAdmin(app, {
      themeOverride: { primary: '#ff5733' },
    })

    const provides = (app as unknown as {
      _context: { provides: Record<symbol, unknown> }
    })._context.provides
    const ctx = provides[THEME_CONTEXT_KEY] as ThemeCtxLike | undefined
    expect(ctx?.settings.value.colors.primary).toBe('#ff5733')

    plugin.uninstall()
  })

  it('respects existing theme context (does not overwrite)', () => {
    const app = createApp(defineComponent({ render: () => h('div') }))
    const existingCtx = {
      settings: { value: { colors: { primary: '#abcdef' } } },
    }
    app.provide(THEME_CONTEXT_KEY, existingCtx as unknown)

    const plugin = createTnziUiAdmin(app, {
      themeOverride: { primary: '#ff5733' }, // should be ignored
    })

    const provides = (app as unknown as {
      _context: { provides: Record<symbol, unknown> }
    })._context.provides
    const ctx = provides[THEME_CONTEXT_KEY] as ThemeCtxLike | undefined
    expect(ctx?.settings.value.colors.primary).toBe('#abcdef')

    plugin.uninstall()
    void mountAndReadCtx
  })
})
