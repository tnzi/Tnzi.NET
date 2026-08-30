import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import DualControl from '../../../src/pages/authorization/DualControl.vue'

const ME = 'user-me'
const OTHER = 'user-other'

const mockApprove = vi.fn(async () => ({ id: 'd1', status: 'Approved' }))
const mockReject = vi.fn(async () => ({ id: 'd1', status: 'Rejected' }))
const mockCancel = vi.fn(async () => undefined)
const mockGetById = vi.fn(async () => row())

function row(overrides: Record<string, unknown> = {}) {
  return {
    id: 'd1',
    operation: 'finance.payrun.void',
    targetId: 'PR-1',
    payloadJson: '{"amount":100}',
    description: 'Void the July pay run',
    status: 'Pending',
    requesterId: OTHER,
    requesterName: 'alice',
    approverId: null,
    approverName: null,
    creationTime: '2026-08-01T00:00:00Z',
    decidedAt: null,
    decisionComment: null,
    expiresAt: '2026-08-02T00:00:00Z',
    isConsumed: false,
    isUsable: false,
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
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/dual-control-bridge', () => ({
  createDualControlBridge: () => ({
    requests: {
      fetch: mockFetch,
      getById: mockGetById,
      approve: mockApprove,
      reject: mockReject,
      cancel: mockCancel,
    },
  }),
}))

// Super-user short-circuits `can()`, so every permission-gated action is visible
// unless a test says otherwise.
vi.mock('../../../src/stores/useAdminAuthStore', () => ({
  useAdminAuthStore: () => ({
    userInfo: { id: ME, permissions: [] },
    isSuperUser: true,
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Popconfirm: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Select: { template: '<select />' },
  Drawer: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { template: '<div><slot /></div>' },
  Alert: { template: '<div><slot /></div>' },
  Spin: { template: '<div><slot /></div>' },
}

async function mountPage() {
  const wrapper = mount(DualControl, { global: { stubs } })
  await nextTick()
  await new Promise((r) => setTimeout(r, 10))
  return wrapper
}

type Vm = {
  rowTags: (r: ReturnType<typeof row>) => { label: string }[]
  detailItems: (r: ReturnType<typeof row>) => { label: string; value: unknown }[]
  prettyPayload: (p?: string | null) => string
  rowActions: { key: string; show?: (r: ReturnType<typeof row>) => boolean }[]
  openDecision: (r: ReturnType<typeof row>, kind: 'approve' | 'reject') => void
  submitDecision: () => Promise<void>
  cancelRequest: (r: ReturnType<typeof row>) => Promise<void>
  decisionComment: string
  decisionShow: boolean
}

describe('Dual-control approval queue', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('renders each request as a row card naming the action and who raised it', async () => {
    const wrapper = await mountPage()
    expect(mockFetch).toHaveBeenCalled()
    expect(wrapper.findAll('.t-item-card')).toHaveLength(1)
    expect(wrapper.text()).toContain('finance.payrun.void')
    // The second pair of eyes has to know WHO asked - a GUID cannot answer that.
    expect(wrapper.text()).toContain('alice')
  })

  it('offers no create, edit or delete - a request is raised by a business action', async () => {
    const wrapper = await mountPage()
    expect(wrapper.find('.t-list-shell__create').exists()).toBe(false)
    expect(wrapper.html()).not.toContain('t-list-shell__batch-delete')
  })

  it('shows the payload snapshot, because that is what is being approved', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm
    // Pretty-printed for reading only; the backend compares the stored string
    // byte for byte and never sees this.
    expect(vm.prettyPayload('{"amount":100}')).toContain('"amount": 100')
    // Unparseable payloads must still be shown, not swallowed.
    expect(vm.prettyPayload('not json')).toBe('not json')
  })

  it('distinguishes a spent permit from a live one', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm

    const live = vm.rowTags(row({ status: 'Approved', isUsable: true }))
    expect(live).toHaveLength(1)

    // Approved but already used: status alone would read as "still good".
    const used = vm.rowTags(row({ status: 'Approved', isUsable: false, isConsumed: true }))
    expect(used).toHaveLength(2)

    const expired = vm.rowTags(row({ status: 'Approved', isUsable: false, isConsumed: false }))
    expect(expired).toHaveLength(2)
  })

  it('only offers approve/reject while the request is still pending', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm
    const approve = vm.rowActions.find((a) => a.key === 'approve')!
    const reject = vm.rowActions.find((a) => a.key === 'reject')!

    expect(approve.show!(row())).toBe(true)
    expect(reject.show!(row())).toBe(true)
    expect(approve.show!(row({ status: 'Approved' }))).toBe(false)
    expect(reject.show!(row({ status: 'Rejected' }))).toBe(false)
  })

  it('offers withdraw only on your own request - the service allows nobody else', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm
    const cancel = vm.rowActions.find((a) => a.key === 'cancel')!

    // Raised by someone else: hiding it keeps the row from offering a button
    // that always 403s.
    expect(cancel.show!(row())).toBe(false)
    expect(cancel.show!(row({ requesterId: ME }))).toBe(true)
    expect(cancel.show!(row({ requesterId: ME, status: 'Approved' }))).toBe(false)
  })

  it('sends the note with an approval and refreshes the list', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm
    vm.openDecision(row(), 'approve')
    vm.decisionComment = 'checked the amounts'
    mockFetch.mockClear()

    await vm.submitDecision()

    expect(mockApprove).toHaveBeenCalledWith('d1', 'checked the amounts')
    expect(mockReject).not.toHaveBeenCalled()
    expect(mockFetch).toHaveBeenCalled()
  })

  it('keeps the dialog open when the server refuses, so the reason stays readable', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm
    mockApprove.mockRejectedValueOnce(new Error('The requester cannot decide their own request'))

    vm.openDecision(row(), 'approve')
    await vm.submitDecision()

    // A 403 here is a normal outcome (you raised it, or you lack
    // `{operation}.approve`) - closing the dialog would hide which one.
    expect(vm.decisionShow).toBe(true)
  })

  it('refreshes after a withdraw so the row stops reading as pending', async () => {
    const wrapper = await mountPage()
    const vm = wrapper.vm as unknown as Vm
    mockFetch.mockClear()

    await vm.cancelRequest(row({ requesterId: ME }))

    expect(mockCancel).toHaveBeenCalledWith('d1')
    expect(mockFetch).toHaveBeenCalled()
  })
})
