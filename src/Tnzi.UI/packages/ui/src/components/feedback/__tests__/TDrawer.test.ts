import { describe, it, expect, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TDrawer from '../TDrawer.vue'

async function flushFocus() {
  await nextTick()
  await new Promise<void>((r) => queueMicrotask(() => r()))
}

describe('TDrawer a11y', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('sets aria-labelledby to the title id when title is provided', async () => {
    const wrapper = mount(TDrawer, {
      props: { show: true, title: 'Settings' },
      attachTo: document.body,
    })
    await nextTick()
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement
    expect(dialog).toBeTruthy()
    const labelledby = dialog.getAttribute('aria-labelledby')
    expect(labelledby).toBeTruthy()
    expect(document.getElementById(labelledby!)?.textContent).toBe('Settings')
    wrapper.unmount()
  })

  it('omits aria-labelledby when no title is provided', async () => {
    const wrapper = mount(TDrawer, {
      props: { show: true },
      attachTo: document.body,
    })
    await nextTick()
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement
    expect(dialog.hasAttribute('aria-labelledby')).toBe(false)
    wrapper.unmount()
  })

  it('emits close + update:show=false when Escape is pressed', async () => {
    const wrapper = mount(TDrawer, {
      props: { show: true, title: 'x' },
      attachTo: document.body,
    })
    await flushFocus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(wrapper.emitted('close')).toHaveLength(1)
    expect(wrapper.emitted('update:show')?.[0]).toEqual([false])
    wrapper.unmount()
  })

  it('focuses the close button on open', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()
    const wrapper = mount(TDrawer, {
      props: { show: true, title: 'x' },
      attachTo: document.body,
    })
    await flushFocus()
    const active = document.activeElement as HTMLElement
    expect(active.classList.contains('t-drawer__close')).toBe(true)
    wrapper.unmount()
  })
})
