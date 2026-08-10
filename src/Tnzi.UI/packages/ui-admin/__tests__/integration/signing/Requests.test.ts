import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Requests from '../../../src/pages/signing/Requests.vue'

const mockSend = vi.fn(async () => [
  { recipientId: 'r1', name: 'Dana Lee', email: 'dana@example.com', token: 'tok-plaintext-1' },
])
const mockVoid = vi.fn(async () => undefined)
const mockGetById = vi.fn(async () => ({ id: 'e1', title: 'NDA', recipients: [] }))

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
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      send: mockSend,
      void: mockVoid,
    },
    templates: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 200 })),
      getById: vi.fn(),
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
})
