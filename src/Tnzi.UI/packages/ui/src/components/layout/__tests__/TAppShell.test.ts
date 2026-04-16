import { describe, it, expect, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, h } from 'vue'
import TAppShell from '../TAppShell.vue'

describe('TAppShell mobile drawer a11y', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('emits update:drawerOpen=false when Escape is pressed', async () => {
    const wrapper = mount(TAppShell, {
      props: { drawerOpen: true },
      slots: {
        'mobile-drawer': () => h('nav', [h('a', { href: '#a' }, 'A')]),
      },
      attachTo: document.body,
    })
    await nextTick()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(wrapper.emitted('update:drawerOpen')?.[0]).toEqual([false])
    wrapper.unmount()
  })

  it('marks the drawer aside with role=dialog + aria-modal + aria-label', async () => {
    const wrapper = mount(TAppShell, {
      props: { drawerOpen: true, drawerLabel: 'Main navigation' },
      slots: { 'mobile-drawer': () => h('nav', 'content') },
      attachTo: document.body,
    })
    await nextTick()
    const aside = document.querySelector('.t-app-shell__drawer')
    expect(aside?.getAttribute('role')).toBe('dialog')
    expect(aside?.getAttribute('aria-modal')).toBe('true')
    expect(aside?.getAttribute('aria-label')).toBe('Main navigation')
    wrapper.unmount()
  })

  it('uses the default drawer aria-label when none is provided', async () => {
    const wrapper = mount(TAppShell, {
      props: { drawerOpen: true },
      slots: { 'mobile-drawer': () => h('nav', 'content') },
      attachTo: document.body,
    })
    await nextTick()
    const aside = document.querySelector('.t-app-shell__drawer')
    expect(aside?.getAttribute('aria-label')).toBe('Navigation')
    wrapper.unmount()
  })
})
