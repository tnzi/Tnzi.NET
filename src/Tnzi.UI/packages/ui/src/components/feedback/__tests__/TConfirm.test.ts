import { describe, it, expect, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TConfirm from '../TConfirm.vue'

async function flushFocus() {
  await nextTick()
  await new Promise<void>((r) => queueMicrotask(() => r()))
}

describe('TConfirm a11y', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('sets aria-labelledby to the title element id', async () => {
    const wrapper = mount(TConfirm, {
      props: { show: true, title: 'Delete?' },
      attachTo: document.body,
    })
    await nextTick()
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement
    expect(dialog).toBeTruthy()
    const labelledby = dialog.getAttribute('aria-labelledby')
    expect(labelledby).toBeTruthy()
    const titleEl = document.getElementById(labelledby!)
    expect(titleEl?.textContent).toBe('Delete?')
    wrapper.unmount()
  })

  it('emits cancel + update:show=false when Escape is pressed', async () => {
    const wrapper = mount(TConfirm, {
      props: { show: true, title: 'Delete?' },
      attachTo: document.body,
    })
    await flushFocus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('update:show')?.[0]).toEqual([false])
    wrapper.unmount()
  })

  it('focuses the primary action button on open', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()
    const wrapper = mount(TConfirm, {
      props: { show: true, title: 'Delete?' },
      attachTo: document.body,
    })
    await flushFocus()
    const active = document.activeElement as HTMLElement
    expect(active.classList.contains('t-confirm__ok')).toBe(true)
    wrapper.unmount()
  })
})
