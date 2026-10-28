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
  Alert: { name: 'Alert', template: '<div class="n-alert-stub"><slot /></div>' },
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

  // A failed read used to render "Books are open / any date can still be posted
  // to" and offer "Set closing date" without the current-password field: a
  // claim about a lock that may well be in force. It now says it does not know.
  it('★ a failed read says the closing date is unknown, never that the books are open', async () => {
    getClosingDate.mockRejectedValue(new Error('Request failed with status code 403'))
    const wrapper = mountPanel()
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('closingDate.unknown')
    expect(text).toContain('Request failed with status code 403')
    expect(text).not.toContain('closingDate.notClosed')
    expect(text).not.toContain('closingDate.openHint')
    // No editor over a lock it could not read (it would not know to ask for the password).
    expect(text).not.toContain('closingDate.set')
    expect(text).not.toContain('closingDate.change')
  })

  it('a failed read can be retried', async () => {
    getClosingDate.mockRejectedValueOnce(new Error('timeout'))
    const wrapper = mountPanel()
    await flushPromises()
    const retry = wrapper.findAll('button').find((b) => b.text() === 'closingDate.retry')
    expect(retry, 'retry button').toBeDefined()
    await retry!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('closingDate.notClosed')
    expect(wrapper.text()).not.toContain('closingDate.unknown')
  })

  // A rejected save (wrong password 403, future date 400) kept the modal open
  // with no message: the error was stored but never rendered, then rethrown
  // from the click handler as an unhandled rejection.
  it('★ a rejected save shows the reason in the editor and settles', async () => {
    setClosingDate.mockRejectedValue(new Error('The closing-date password is incorrect.'))
    const wrapper = mountPanel()
    await flushPromises()
    const vm = wrapper.vm as unknown as PanelVm
    vm.openEditor()
    vm.draftDate = new Date(2026, 5, 30).getTime()
    await expect(vm.save()).resolves.toBeUndefined()
    await flushPromises()
    expect(vm.editing).toBe(true)
    expect(wrapper.find('.n-alert-stub').text()).toContain('The closing-date password is incorrect.')
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
