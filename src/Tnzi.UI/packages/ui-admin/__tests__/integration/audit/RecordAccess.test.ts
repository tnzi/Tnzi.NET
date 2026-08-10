import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const fetchMock = vi.fn()
const userStatisticsMock = vi.fn()
const verifyMock = vi.fn()

vi.mock('../../../src/services/bridges/audit-bridge', () => ({
  createAuditBridge: () => ({
    logs: {},
    operations: {},
    recordAccess: {
      fetch: fetchMock,
      userStatistics: userStatisticsMock,
      verify: verifyMock,
    },
    destruction: {},
  }),
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))

const successMock = vi.fn()
const errorMock = vi.fn()
vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => ({ success: successMock, error: errorMock, warning: vi.fn(), info: vi.fn() }),
}))

const stubs = {
  TCrudPage: {
    props: ['state'],
    template: '<div class="crud"><slot name="toolbarRight" /><slot name="detail" :data="null" /></div>',
  },
  TModalShell: { template: '<div class="modal"><slot /></div>' },
  TDescriptions: { template: '<div class="descriptions" />' },
  TEmpty: { template: '<div class="empty" />' },
  TSvgIcon: { template: '<i />' },
  NButton: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  NDataTable: { template: '<div class="data-table" />' },
}

async function mountPage() {
  const RecordAccess = (await import('../../../src/pages/audit/RecordAccess.vue')).default
  return mount(RecordAccess, { global: { stubs } })
}

describe('audit/RecordAccess', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    fetchMock.mockResolvedValue({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
    userStatisticsMock.mockResolvedValue([])
    verifyMock.mockResolvedValue(undefined)
  })

  it('mounts and fetches the read trail', async () => {
    const wrapper = await mountPage()
    await new Promise((r) => setTimeout(r, 0))

    expect(wrapper.find('.crud').exists()).toBe(true)
    expect(fetchMock).toHaveBeenCalled()
  })

  it('verify reports a broken chain with the backend message', async () => {
    // A broken chain is a security finding: the message names the first bad
    // sequence, so a generic toast would throw away the only useful detail.
    verifyMock.mockRejectedValueOnce(new Error('chain broken at sequence 7'))
    const wrapper = await mountPage()
    await (wrapper.vm as unknown as { verifyChain: () => Promise<void> }).verifyChain()

    expect(errorMock).toHaveBeenCalledWith('chain broken at sequence 7')
    expect(successMock).not.toHaveBeenCalled()
  })

  it('loads read-volume statistics on demand, not on mount', async () => {
    const wrapper = await mountPage()
    await new Promise((r) => setTimeout(r, 0))
    expect(userStatisticsMock).not.toHaveBeenCalled()

    await (wrapper.vm as unknown as { openStatistics: () => Promise<void> }).openStatistics()
    expect(userStatisticsMock).toHaveBeenCalled()
  })
})
