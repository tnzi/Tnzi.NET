import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { ref, nextTick, defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { useFocusTrap } from '../useFocusTrap'

function createHost() {
  return defineComponent({
    props: { active: { type: Boolean, required: true } },
    setup(props) {
      const rootRef = ref<HTMLElement | null>(null)
      const onEscape = vi.fn()
      useFocusTrap(rootRef, () => props.active, { onEscape })
      return { rootRef, onEscape }
    },
    render() {
      return h('div', { ref: 'rootRef', 'data-testid': 'root' }, [
        h('button', { 'data-testid': 'first' }, 'first'),
        h('input', { 'data-testid': 'middle' }),
        h('button', { 'data-testid': 'last' }, 'last'),
      ])
    },
  })
}

async function flushFocus() {
  await nextTick()
  await new Promise<void>((r) => queueMicrotask(() => r()))
}

describe('useFocusTrap', () => {
  let triggerBtn: HTMLButtonElement
  beforeEach(() => {
    triggerBtn = document.createElement('button')
    triggerBtn.textContent = 'trigger'
    document.body.appendChild(triggerBtn)
    triggerBtn.focus()
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('focuses the first focusable element on activation', async () => {
    const Host = createHost()
    const wrapper = mount(Host, { props: { active: true }, attachTo: document.body })
    await flushFocus()
    expect(document.activeElement).toBe(wrapper.find('[data-testid="first"]').element)
    wrapper.unmount()
  })

  it('calls onEscape when Escape is pressed while active', async () => {
    const Host = createHost()
    const wrapper = mount(Host, { props: { active: true }, attachTo: document.body })
    await flushFocus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(wrapper.vm.onEscape).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('does not call onEscape when inactive', async () => {
    const Host = createHost()
    const wrapper = mount(Host, { props: { active: false }, attachTo: document.body })
    await flushFocus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(wrapper.vm.onEscape).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('wraps Tab from last focusable back to first', async () => {
    const Host = createHost()
    const wrapper = mount(Host, { props: { active: true }, attachTo: document.body })
    await flushFocus()
    const first = wrapper.find('[data-testid="first"]').element as HTMLButtonElement
    const last = wrapper.find('[data-testid="last"]').element as HTMLButtonElement
    last.focus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))
    await nextTick()
    expect(document.activeElement).toBe(first)
    wrapper.unmount()
  })

  it('wraps Shift+Tab from first focusable to last', async () => {
    const Host = createHost()
    const wrapper = mount(Host, { props: { active: true }, attachTo: document.body })
    await flushFocus()
    const first = wrapper.find('[data-testid="first"]').element as HTMLButtonElement
    const last = wrapper.find('[data-testid="last"]').element as HTMLButtonElement
    first.focus()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }))
    await nextTick()
    expect(document.activeElement).toBe(last)
    wrapper.unmount()
  })

  it('restores focus to the previously focused element on deactivation', async () => {
    const Host = createHost()
    const wrapper = mount(Host, { props: { active: true }, attachTo: document.body })
    await flushFocus()
    await wrapper.setProps({ active: false })
    await flushFocus()
    expect(document.activeElement).toBe(triggerBtn)
    wrapper.unmount()
  })
})
