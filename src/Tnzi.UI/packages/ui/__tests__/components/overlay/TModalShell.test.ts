import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TModalShell from '../../../src/components/overlay/TModalShell.vue'

// Capture the props naive's NModal receives so we can assert the chrome
// defaults (size, mask-closable) the shell forwards.
const modalStub = {
  name: 'Modal',
  props: ['show', 'size', 'maskClosable', 'preset', 'title', 'style'],
  template: '<div class="n-modal-stub" :data-size="size" :data-preset="preset" v-if="show"><slot name="header" /><slot /><slot name="footer" /></div>',
}

const stubs = { Modal: modalStub }

describe('TModalShell', () => {
  it('renders a card-preset NModal when shown', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    const modal = w.find('.n-modal-stub')
    expect(modal.exists()).toBe(true)
    expect(modal.attributes('data-preset')).toBe('card')
  })

  it('defaults to the compact `small` card size (tighter padding)', () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(w.find('.n-modal-stub').attributes('data-size')).toBe('small')
  })

  it('honours an explicit size override', () => {
    const w = mount(TModalShell, { props: { show: true, size: 'medium' }, global: { stubs } })
    expect(w.find('.n-modal-stub').attributes('data-size')).toBe('medium')
  })

  it('wraps the body in the scroll region', () => {
    const w = mount(TModalShell, {
      props: { show: true },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-modal-shell__scroll .body').exists()).toBe(true)
  })

  it('renders the footer region only when a footer slot is supplied', () => {
    const without = mount(TModalShell, { props: { show: true }, global: { stubs } })
    expect(without.find('.foot').exists()).toBe(false)
    const withFooter = mount(TModalShell, {
      props: { show: true },
      slots: { footer: '<div class="foot" />' },
      global: { stubs },
    })
    expect(withFooter.find('.foot').exists()).toBe(true)
  })

  it('emits update:show when the modal requests close', async () => {
    const w = mount(TModalShell, { props: { show: true }, global: { stubs } })
    w.findComponent(modalStub).vm.$emit('update:show', false)
    await w.vm.$nextTick()
    expect(w.emitted('update:show')?.[0]).toEqual([false])
  })

  it('renders a #header slot for rich titles (entity name + tag)', () => {
    const w = mount(TModalShell, {
      props: { show: true },
      slots: { header: '<div class="rich-head">Invoice INV-001 <span class="tag">Posted</span></div>' },
      global: { stubs },
    })
    expect(w.find('.rich-head').exists()).toBe(true)
    expect(w.find('.rich-head .tag').text()).toBe('Posted')
  })

  it('overlays the body with a spinner while loading', () => {
    const w = mount(TModalShell, {
      props: { show: true, loading: true },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    // Body stays mounted under the overlay; the scroll region gets the
    // min-height guard so an empty body doesn't collapse.
    expect(w.find('.t-modal-shell__scroll--loading').exists()).toBe(true)
    expect(w.find('.n-spin-body').exists()).toBe(true)
    expect(w.find('.t-modal-shell__scroll .body').exists()).toBe(true)
  })

  it('shows no spinner or min-height guard when not loading', () => {
    const w = mount(TModalShell, {
      props: { show: true },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-modal-shell__scroll--loading').exists()).toBe(false)
    expect(w.find('.n-spin-body').exists()).toBe(false)
    expect(w.find('.t-modal-shell__scroll .body').exists()).toBe(true)
  })
})
