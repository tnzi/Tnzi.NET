import { describe, it, expect, afterEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import { NDialogProvider, useDialog } from 'naive-ui'
import { createDialogAdapter } from '../../src/adapters/dialog'

// The mocked-api tests in adapters.test.ts only prove which callback the adapter
// answers to. This file proves the premise: under a real NDialogProvider, Esc and a
// mask click close the dialog through onAfterLeave and never through onClose.

let wrapper: VueWrapper | null = null

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  delete (window as unknown as Record<string, unknown>).$dialog
})

function mountProvider() {
  const Handle = defineComponent({
    setup() {
      ;(window as unknown as Record<string, unknown>).$dialog = useDialog()
      return () => h('div')
    },
  })
  wrapper = mount(NDialogProvider, {
    slots: { default: () => h(Handle) },
    attachTo: document.body,
    // test-utils stubs <Transition> by default, and a stubbed transition never runs
    // its leave hooks - the very hook this adapter relies on.
    global: { stubs: { transition: false } },
  })
}

async function openDialog(open: () => Promise<unknown>) {
  const pending = open()
  await nextTick()
  await nextTick()
  expect(document.querySelector('.n-dialog'), 'the naive dialog must be in the DOM').not.toBeNull()
  // Boxed on purpose: `await openDialog()` would otherwise flatten and wait on the dialog itself.
  return { pending }
}

async function settled(promise: Promise<unknown>) {
  // The leave transition resolves through requestAnimationFrame; drain a few frames
  // before racing so a still-pending promise is a real hang, not a slow frame.
  for (let i = 0; i < 5; i++) {
    await new Promise((r) => setTimeout(r, 20))
    await nextTick()
  }
  return Promise.race([promise, new Promise((r) => setTimeout(() => r('pending'), 50))])
}

describe('adapters/dialog under a real NDialogProvider', () => {
  it('confirm settles false when the user presses Escape', async () => {
    mountProvider()
    const { pending } = await openDialog(() => createDialogAdapter().confirm('Delete?'))

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true }))

    expect(await settled(pending)).toBe(false)
    expect(document.querySelector('.n-dialog')).toBeNull()
  })

  it('prompt settles null when the user clicks the mask', async () => {
    mountProvider()
    const { pending } = await openDialog(() => createDialogAdapter().prompt('Name?'))

    const mask = document.querySelector('.n-modal-mask') as HTMLElement
    expect(mask).not.toBeNull()
    mask.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
    mask.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
    mask.click()

    expect(await settled(pending)).toBeNull()
  })
})
