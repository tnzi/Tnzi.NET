import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'

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

/** The row whose detail drawer is open (null = drawer closed). */
const openRow = ref<Record<string, unknown> | null>(null)

const stubs = {
  TCrudPage: {
    props: ['state'],
    setup: () => ({ openRow }),
    template:
      '<div class="crud"><div class="toolbar"><slot name="toolbarRight" /></div>' +
      '<slot name="detail" :data="openRow" />' +
      '<div class="detail-footer"><slot name="detailFooter" :data="openRow" /></div></div>',
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

const namedRead = { id: 'r1', sequence: 4, resourceType: 'Matter', resourceId: 'm-1', userId: 'u-42', userName: 'alice', hash: 'h', creationTime: '2026-10-10T00:00:00Z' }
const anonymousRead = { ...namedRead, id: 'r2', userId: null, userName: null }

describe('audit/RecordAccess', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    openRow.value = null
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

  it('offers no list-level verify: chains are per reader and none is selected there', async () => {
    // From the list the only chain a verify call could check is the anonymous
    // one, and a green "intact" over that reads as "the whole trail is intact".
    const wrapper = await mountPage()
    expect(wrapper.find('.toolbar').text()).not.toContain('Verify')
  })

  it("verifies the open row's reader and names that reader in the result", async () => {
    openRow.value = namedRead
    const wrapper = await mountPage()
    await flushPromises()

    const verify = wrapper.find('.detail-footer button')
    expect(verify.exists()).toBe(true)
    await verify.trigger('click')
    await flushPromises()

    expect(verifyMock).toHaveBeenCalledWith('u-42')
    expect(String(successMock.mock.calls.at(-1)?.[0])).toContain('alice')
  })

  it('an anonymous row verifies the anonymous chain and says that is what was checked', async () => {
    openRow.value = anonymousRead
    const wrapper = await mountPage()
    await flushPromises()

    await wrapper.find('.detail-footer button').trigger('click')
    await flushPromises()

    expect(verifyMock).toHaveBeenCalledWith(undefined)
    expect(String(successMock.mock.calls.at(-1)?.[0]).toLowerCase()).toContain('anonymous')
  })

  it('verify reports a broken chain with the backend message', async () => {
    // A broken chain is a security finding: the message names the first bad
    // sequence, so a generic toast would throw away the only useful detail.
    verifyMock.mockRejectedValueOnce(new Error('chain broken at sequence 7'))
    openRow.value = namedRead
    const wrapper = await mountPage()
    await flushPromises()
    await wrapper.find('.detail-footer button').trigger('click')
    await flushPromises()

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
