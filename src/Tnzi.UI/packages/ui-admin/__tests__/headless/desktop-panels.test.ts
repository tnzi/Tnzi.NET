import { describe, it, expect, afterEach } from 'vitest'
import { defineComponent } from 'vue'
import {
  registerDesktopPanel,
  getDesktopPanel,
  getDesktopPanelTint,
  unregisterDesktopPanel,
} from '../../src/headless/desktop-panels'

const Panel = defineComponent({ render: () => null })

describe('desktop panel registry', () => {
  afterEach(() => unregisterDesktopPanel('probe'))

  it('hands back what was registered', () => {
    registerDesktopPanel('probe', Panel, { color: '#06c863' })
    expect(getDesktopPanel('probe')).toBe(Panel)
    expect(getDesktopPanelTint('probe')).toBe('#06c863')
  })

  it('reports nothing for an unregistered key', () => {
    // The window host renders a "missing" state rather than an empty frame.
    expect(getDesktopPanel('nope')).toBeNull()
    expect(getDesktopPanelTint('nope')).toBeNull()
  })

  it('lets a re-registration replace the previous one', () => {
    // What a hot reload does.
    registerDesktopPanel('probe', Panel)
    expect(getDesktopPanelTint('probe')).toBeNull()
    registerDesktopPanel('probe', Panel, { color: '#123456' })
    expect(getDesktopPanelTint('probe')).toBe('#123456')
  })
})
