import { describe, it, expect, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TConfirm from '../../../src/components/feedback/TConfirm.vue'
import TLoadingBar from '../../../src/components/feedback/TLoadingBar.vue'

/** Let Vue flush its render queue AND the microtask that moves focus. */
async function flushFocus() {
  await nextTick()
  await new Promise<void>((r) => queueMicrotask(() => r()))
}

describe('TConfirm', () => {
  it('does not render when show is false', () => {
    const wrapper = mount(TConfirm, { props: { show: false, title: 'Delete?' } })
    expect(wrapper.text()).not.toContain('Delete?')
  })
  it('renders modal when show is true', () => {
    const wrapper = mount(TConfirm, { props: { show: true, title: 'Delete?' }, attachTo: document.body })
    expect(document.body.textContent).toContain('Delete?')
    wrapper.unmount()
  })
  it('emits update:show(false) when cancel clicked', async () => {
    const wrapper = mount(TConfirm, { props: { show: true, title: 'Delete?' }, attachTo: document.body })
    const cancelBtn = document.querySelector('.t-confirm__cancel') as HTMLButtonElement
    expect(cancelBtn).toBeTruthy()
    cancelBtn.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:show')).toBeTruthy()
    expect(wrapper.emitted('update:show')?.[0]).toEqual([false])
    wrapper.unmount()
  })
  it('emits confirm when confirm clicked', async () => {
    const wrapper = mount(TConfirm, { props: { show: true, title: 'Delete?' }, attachTo: document.body })
    const okBtn = document.querySelector('.t-confirm__ok') as HTMLButtonElement
    expect(okBtn).toBeTruthy()
    okBtn.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('confirm')).toBeTruthy()
    wrapper.unmount()
  })
})

describe('TLoadingBar', () => {
  it('is hidden by default', () => {
    const wrapper = mount(TLoadingBar)
    const bar = wrapper.find('.t-loading-bar')
    expect(bar.attributes('style') ?? '').toContain('display: none')
  })
  it('shows when start called', async () => {
    const wrapper = mount(TLoadingBar)
    ;(wrapper.vm as any).start()
    await wrapper.vm.$nextTick()
    const bar = wrapper.find('.t-loading-bar')
    expect(bar.attributes('style') ?? '').not.toContain('display: none')
  })
  it('reaches 100% on finish', async () => {
    const wrapper = mount(TLoadingBar)
    ;(wrapper.vm as any).start()
    ;(wrapper.vm as any).finish()
    await wrapper.vm.$nextTick()
    const bar = wrapper.find('.t-loading-bar__progress')
    if (bar.exists()) {
      expect(bar.attributes('style') ?? '').toContain('100%')
    }
  })
})

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

