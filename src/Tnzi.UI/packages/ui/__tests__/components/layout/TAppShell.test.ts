import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, h } from 'vue'
import TAppShell from '../../../src/components/layout/TAppShell.vue'

describe('TAppShell', () => {
  it('renders header, main, footer in order', () => {
    const wrapper = mount(TAppShell, {
      slots: {
        header: '<div class="h">H</div>',
        default: '<div class="m">M</div>',
        footer: '<div class="f">F</div>',
      },
    })
    const html = wrapper.html()
    const hIdx = html.indexOf('class="h"')
    const mIdx = html.indexOf('class="m"')
    const fIdx = html.indexOf('class="f"')
    expect(hIdx).toBeLessThan(mIdx)
    expect(mIdx).toBeLessThan(fIdx)
  })

  it('main takes full remaining height', () => {
    const wrapper = mount(TAppShell, { slots: { default: 'main' } })
    expect(wrapper.find('main').attributes('style')).toContain('flex-grow: 1')
  })

  it('mobile drawer slot renders outside main tree', () => {
    const wrapper = mount(TAppShell, {
      attachTo: document.body,
      props: { drawerOpen: true },
      slots: {
        'mobile-drawer': '<div class="mobile-drawer-content">Drawer</div>',
      },
    })
    expect(document.body.querySelector('.mobile-drawer-content')).not.toBeNull()
    wrapper.unmount()
  })

  it('drawer is hidden by default', () => {
    const wrapper = mount(TAppShell, {
      attachTo: document.body,
      slots: {
        'mobile-drawer': '<div class="drawer-inner">x</div>',
      },
    })
    expect(document.body.querySelector('.t-app-shell__drawer--open')).toBeNull()
    wrapper.unmount()
  })

  it('drawer opens when drawerOpen prop is true', () => {
    const wrapper = mount(TAppShell, {
      attachTo: document.body,
      props: { drawerOpen: true },
      slots: {
        'mobile-drawer': '<div>d</div>',
      },
    })
    expect(document.body.querySelector('.t-app-shell__drawer--open')).not.toBeNull()
    wrapper.unmount()
  })

  it('emits update:drawerOpen when backdrop clicked', async () => {
    const wrapper = mount(TAppShell, {
      attachTo: document.body,
      props: { drawerOpen: true },
      slots: { 'mobile-drawer': '<div>d</div>' },
    })
    const backdrop = document.body.querySelector('.t-app-shell__backdrop') as HTMLElement | null
    expect(backdrop).not.toBeNull()
    backdrop!.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:drawerOpen')).toBeTruthy()
    expect(wrapper.emitted('update:drawerOpen')?.[0]).toEqual([false])
    wrapper.unmount()
  })
})

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
