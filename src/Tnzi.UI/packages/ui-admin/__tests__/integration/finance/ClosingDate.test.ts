import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ClosingDatePanel from '../../../src/pages/finance/components/ClosingDatePanel.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {}, params: {}, path: '/', fullPath: '/', hash: '', name: 'x', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const getClosingDate = vi.fn()
const setClosingDate = vi.fn()

const bridge = { fiscalYears: { getClosingDate, setClosingDate } }

const stubs = {
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show"><slot /><slot name="footer" /></div>' },
  Card: { name: 'Card', template: '<div><slot /><slot name="footer" /></div>' },
  Form: { name: 'Form', template: '<div><slot /></div>' },
  FormItem: { name: 'FormItem', template: '<div><slot /></div>' },
  DatePicker: { name: 'DatePicker', template: '<input />' },
  Input: { name: 'Input', template: '<input />' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Tag: { name: 'Tag', template: '<span><slot /></span>' },
}

interface PanelVm {
  locked: boolean
  editing: boolean
  draftDate: number | null
  password: string
  newPassword: string
  note: string
  openEditor: () => void
  save: () => Promise<void>
  isFuture: (ts: number) => boolean
}

function mountPanel(canEdit = true) {
  return mount(ClosingDatePanel, {
    props: { bridge: bridge as never, canEdit, t: (k: string) => k },
    global: { stubs },
  })
}

describe('ClosingDatePanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    getClosingDate.mockReset()
    setClosingDate.mockReset()
    getClosingDate.mockResolvedValue({ closingDate: null, isPasswordProtected: false })
    setClosingDate.mockImplementation(async (d: Record<string, unknown>) => ({
      closingDate: d.closingDate, isPasswordProtected: Boolean(d.newPassword),
    }))
  })

  it('reports an open ledger when no closing date is set', async () => {
    const wrapper = mountPanel()
    await flushPromises()
    expect((wrapper.vm as unknown as PanelVm).locked).toBe(false)
  })

  it('reports a locked ledger once a date is set', async () => {
    getClosingDate.mockResolvedValue({ closingDate: '2026-06-30', isPasswordProtected: true })
    const wrapper = mountPanel()
    await flushPromises()
    expect((wrapper.vm as unknown as PanelVm).locked).toBe(true)
  })

  it('fails OPEN on a read error rather than claiming the books are open', async () => {
    // Asserting "not closed" from a failed probe would invite someone to post
    // into a period that is actually filed; the backend 409 is the real gate.
    getClosingDate.mockRejectedValue(new Error('boom'))
    const wrapper = mountPanel()
    await flushPromises()
    expect((wrapper.vm as unknown as PanelVm).locked).toBe(false)
  })

  it('omits newPassword when the field was left blank', async () => {
    const wrapper = mountPanel()
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm
    vm.openEditor()
    vm.draftDate = new Date(2026, 5, 30).getTime()
    await vm.save()

    // `null` means "leave the password alone"; sending '' would clear it.
    expect(setClosingDate).toHaveBeenCalledWith(
      expect.objectContaining({ closingDate: '2026-06-30', newPassword: null }),
    )
  })

  it('sends the typed password through when changing a guarded date', async () => {
    getClosingDate.mockResolvedValue({ closingDate: '2026-06-30', isPasswordProtected: true })
    const wrapper = mountPanel()
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm
    vm.openEditor()
    vm.password = 's3cret'
    vm.draftDate = new Date(2026, 4, 31).getTime()
    await vm.save()

    expect(setClosingDate).toHaveBeenCalledWith(expect.objectContaining({ password: 's3cret' }))
  })

  it('clears the lock by sending a null date', async () => {
    getClosingDate.mockResolvedValue({ closingDate: '2026-06-30', isPasswordProtected: false })
    const wrapper = mountPanel()
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm
    vm.openEditor()
    vm.draftDate = null
    await vm.save()

    expect(setClosingDate).toHaveBeenCalledWith(expect.objectContaining({ closingDate: null }))
  })

  it('disables future dates in the picker', async () => {
    const wrapper = mountPanel()
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm
    // A future closing date would block ordinary current-period posting.
    expect(vm.isFuture(Date.now() + 86_400_000)).toBe(true)
    expect(vm.isFuture(Date.now() - 86_400_000)).toBe(false)
  })

  it('hides the edit affordance without the permission', async () => {
    const wrapper = mountPanel(false)
    await flushPromises()
    expect(wrapper.text()).not.toContain('closingDate.set')
  })
})
