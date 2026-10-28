import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Requests from '../../../src/pages/signing/Requests.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

const mockSend = vi.fn(async () => [
  { recipientId: 'r1', name: 'Dana Lee', email: 'dana@example.com', token: 'tok-plaintext-1' },
])
const mockVoid = vi.fn(async () => undefined)
const mockSeal = vi.fn(async () => ({ id: 'e1', title: 'NDA', recipients: [] }))
const mockCreate = vi.fn(async (data: unknown) => ({ id: 'e2', ...(data as object) }))
const mockGetById = vi.fn(async () => ({ id: 'e1', title: 'NDA', recipients: [] }))

/** A template with one field of each kind the create form has to tell apart. */
const mockGetTemplate = vi.fn(async (id: string) => ({
  id,
  name: 'Quote',
  fields: [
    // Sender-filled, required, unbound: the form must ask for it.
    { key: 'price', label: 'Price', type: 'Text', recipientRole: null, binding: null, required: true },
    // Sender-filled but bound to a merge variable: optional on the form.
    { key: 'client', label: 'Client name', type: 'Text', recipientRole: '', binding: 'ClientName', required: true },
    // A recipient's field and a signature: never on the create form.
    { key: 'note', label: 'Note', type: 'Text', recipientRole: 'Client', binding: null, required: true },
    { key: 'sig', label: 'Signature', type: 'Signature', recipientRole: null, binding: null, required: true },
  ],
}))

function row(overrides: Record<string, unknown> = {}) {
  return {
    id: 'e1',
    title: 'NDA - Acme',
    status: 'Sent',
    isSequential: true,
    expiresAt: '2026-09-01T00:00:00Z',
    completedAt: null,
    creationTime: '2026-08-01T00:00:00Z',
    recipientCount: 2,
    signedCount: 1,
    hostEntityType: 'Matter',
    ...overrides,
  }
}

const mockFetch = vi.fn(async () => ({
  items: [row()] as never[],
  totalCount: 1,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/signing-bridge', () => ({
  createSigningBridge: () => ({
    requests: {
      fetch: mockFetch,
      getById: mockGetById,
      create: mockCreate,
      update: vi.fn(),
      delete: vi.fn(),
      send: mockSend,
      void: mockVoid,
      seal: mockSeal,
    },
    templates: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })),
      getById: mockGetTemplate,
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  InputGroup: { template: '<div><slot /></div>' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button />' },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
  Drawer: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { template: '<div><slot /></div>' },
  Progress: { template: '<div class="prog" />' },
  Alert: { template: '<div><slot /></div>' },
  Spin: { template: '<div><slot /></div>' },
}

async function mountPage() {
  const wrapper = mount(Requests, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
  return wrapper
}

describe('Signing Requests page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('renders each request as a row card carrying its signing progress', async () => {
    const wrapper = await mountPage()
    expect(mockFetch).toHaveBeenCalled()
    expect(wrapper.findAll('.t-item-card')).toHaveLength(1)
    expect(wrapper.text()).toContain('NDA - Acme')
    // "1 of 2 signed" is the question this list exists to answer.
    expect(wrapper.text()).toContain('1')
    expect(wrapper.text()).toContain('2')
  })

  // The status is a string enum whose value is the member name; printing it left
  // the zh-cn list reading 'Sent' / 'InProgress' on every row.
  it('labels the request status in the active locale', async () => {
    useAdminAppStore().setLocale('zh-cn')
    const wrapper = await mountPage()
    expect(wrapper.find('.t-item-card').text()).toContain('已发出')
  })

  it('offers no edit or delete: the snapshot is frozen and a dispatched request is evidence', async () => {
    const wrapper = await mountPage()
    // No updateData / deleteData callbacks → the shell must not render either
    // affordance. Void is the supported way to call a request off.
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
    expect(wrapper.html()).not.toContain('t-list-shell__batch-delete')
  })

  it('surfaces the plaintext tokens after sending, because they are never shown again', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as {
      sendRequest: (r: unknown) => Promise<void>
      issuedLinks: { token: string }[]
      linksShow: boolean
    }
    await vm.sendRequest(row({ status: 'Draft' }))
    expect(mockSend).toHaveBeenCalledWith('e1')
    expect(vm.linksShow).toBe(true)
    expect(vm.issuedLinks.map((l) => l.token)).toEqual(['tok-plaintext-1'])
  })

  it('drops the tokens from memory once the overlay closes', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as {
      sendRequest: (r: unknown) => Promise<void>
      onLinksToggle: (show: boolean) => void
      issuedLinks: unknown[]
    }
    await vm.sendRequest(row({ status: 'Draft' }))
    expect(vm.issuedLinks).toHaveLength(1)
    // Single-use credentials, not page state worth keeping around.
    vm.onLinksToggle(false)
    expect(vm.issuedLinks).toHaveLength(0)
  })

  it('refreshes the list after voiding so the row stops reading as pending', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as { voidRequest: (r: unknown) => Promise<void> }
    mockFetch.mockClear()
    await vm.voidRequest(row())
    expect(mockVoid).toHaveBeenCalledWith('e1')
    expect(mockFetch).toHaveBeenCalled()
  })
  type RowActionLike = { key: string; show?: (r: unknown) => boolean; onClick: (r: unknown) => void }

  function visibleActions(vm: { rowActions: RowActionLike[] }, status: string): string[] {
    return vm.rowActions.filter((a) => !a.show || a.show(row({ status, signedCount: 2 }))).map((a) => a.key)
  }

  it('offers nothing that re-sends a request that has gone out: send only works on drafts', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as { rowActions: RowActionLike[] }

    // The backend answers 409 to send on anything but a draft, and has no
    // re-issue path - an action that calls it on these rows can only ever fail.
    for (const status of ['Sent', 'InProgress']) {
      const keys = visibleActions(vm, status)
      expect(keys).not.toContain('send')
      expect(keys).not.toContain('resend')
    }
    expect(visibleActions(vm, 'Draft')).toContain('send')
  })

  it('offers to finish archiving a completed request, through the seal endpoint', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as { rowActions: RowActionLike[]; finishRequest: (r: unknown) => Promise<void> }

    expect(visibleActions(vm, 'Completed')).toContain('finish')
    expect(visibleActions(vm, 'InProgress')).not.toContain('finish')

    await vm.finishRequest(row({ status: 'Completed' }))
    expect(mockSeal).toHaveBeenCalledWith('e1')
  })

  describe('sender values on the create form', () => {
    type CreateVm = {
      crud: { openCreate: () => void; formModal: { formData: { value: Record<string, unknown> | null } } }
      senderFields: { key: string }[]
      toCreate: (data: unknown) => { prefilledValues?: Record<string, string | null> }
      missingSenderValues: (data: unknown) => string[]
    }

    async function pickTemplate(wrapper: Awaited<ReturnType<typeof mountPage>>, templateId: string): Promise<CreateVm> {
      const vm = wrapper.vm as unknown as CreateVm
      vm.crud.openCreate()
      vm.crud.formModal.formData.value = { ...(vm.crud.formModal.formData.value ?? {}), templateId }
      await nextTick()
      await new Promise((r) => setTimeout(r, 10))
      return vm
    }

    it('asks for the fields the template leaves to the sender, and nothing else', async () => {
      const vm = await pickTemplate(await mountPage(), 'tpl-1')

      expect(mockGetTemplate).toHaveBeenCalledWith('tpl-1')
      // The recipient's field and the signature belong to a recipient.
      expect(vm.senderFields.map((f) => f.key)).toEqual(['price', 'client'])
    })

    it('sends the typed values as prefilledValues, dropping keys that are not sender fields of this template', async () => {
      const vm = await pickTemplate(await mountPage(), 'tpl-1')

      const payload = vm.toCreate({
        templateId: 'tpl-1',
        recipients: [{ role: 'Client', name: 'Dana' }],
        prefilledValues: { price: '1200', client: '', stale: 'from another template' },
      })

      expect(payload.prefilledValues).toEqual({ price: '1200' })
    })

    it('refuses to create a draft whose required, unbound sender field is empty, since it could never be sent', async () => {
      const wrapper = await mountPage()
      const vm = await pickTemplate(wrapper, 'tpl-1')

      // The bound field may come from the linked record; only the unbound one is missing.
      expect(vm.missingSenderValues({ templateId: 'tpl-1' })).toEqual(['Price'])
      expect(vm.missingSenderValues({ templateId: 'tpl-1', prefilledValues: { price: '1200' } })).toEqual([])
    })

    it('sends no prefilledValues for a template without sender fields', async () => {
      mockGetTemplate.mockResolvedValueOnce({ id: 'tpl-2', name: 'NDA', fields: [] } as never)
      const vm = await pickTemplate(await mountPage(), 'tpl-2')

      expect(vm.senderFields).toEqual([])
      expect(vm.toCreate({ templateId: 'tpl-2', prefilledValues: { price: '1' } }).prefilledValues).toBeUndefined()
    })
  })
})
