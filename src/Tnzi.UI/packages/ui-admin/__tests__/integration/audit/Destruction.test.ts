import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const fetchMock = vi.fn()
const verifyMock = vi.fn()
const runMock = vi.fn()

vi.mock('../../../src/services/bridges/audit-bridge', () => ({
  createAuditBridge: () => ({
    logs: {},
    operations: {},
    recordAccess: {},
    destruction: { fetch: fetchMock, verify: verifyMock, run: runMock },
  }),
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({}) }))

const successMock = vi.fn()
const errorMock = vi.fn()
vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => ({ success: successMock, error: errorMock, warning: vi.fn(), info: vi.fn() }),
}))

let hasExecute = true
vi.mock('../../../src/stores/useAdminAuthStore', () => ({
  useAdminAuthStore: () => ({ hasPermission: () => hasExecute }),
}))

const stubs = {
  TCrudPage: {
    props: ['state'],
    template: '<div class="crud"><slot name="toolbarRight" /><slot name="detail" :data="null" /></div>',
  },
  TModalShell: { template: '<div class="modal"><slot /><slot name="footer" /></div>' },
  TDescriptions: { template: '<div class="descriptions" />' },
  TSvgIcon: { template: '<i />' },
  NButton: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
}

async function mountPage() {
  const Destruction = (await import('../../../src/pages/audit/Destruction.vue')).default
  return mount(Destruction, { global: { stubs } })
}

describe('audit/Destruction', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    hasExecute = true
    fetchMock.mockResolvedValue({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
    verifyMock.mockResolvedValue(undefined)
    runMock.mockResolvedValue({ policies: [], totalDestroyed: 0, totalHeld: 0, isDryRun: false })
  })

  it('mounts and fetches certificates', async () => {
    const wrapper = await mountPage()
    await new Promise((r) => setTimeout(r, 0))

    expect(wrapper.find('.crud').exists()).toBe(true)
    expect(fetchMock).toHaveBeenCalled()
  })

  it('hides the run button without audit.destruction.execute', async () => {
    // Running a cycle permanently deletes data - viewing certificates is a far
    // weaker right and must not imply it.
    hasExecute = false
    const wrapper = await mountPage()

    expect((wrapper.vm as unknown as { canExecute: boolean }).canExecute).toBe(false)
  })

  it('surfaces per-policy failures instead of only the overall total', async () => {
    // A run that reports "0 destroyed" while one policy blew up looks like
    // "nothing was due" unless the failure is said out loud.
    runMock.mockResolvedValueOnce({
      policies: [{ policyName: 'tip-retention', error: 'entity not mapped' }],
      totalDestroyed: 0,
      totalHeld: 0,
      isDryRun: false,
    })
    const wrapper = await mountPage()
    await (wrapper.vm as unknown as { runNow: () => Promise<void> }).runNow()

    expect(errorMock).toHaveBeenCalledWith('tip-retention: entity not mapped')
  })

  it('normalises the dry-run filter from the select string back to a boolean', async () => {
    const wrapper = await mountPage()
    const normalise = (wrapper.vm as unknown as {
      normaliseFilters: (f: Record<string, unknown>) => Record<string, unknown>
    }).normaliseFilters

    expect(normalise({ isDryRun: 'true' })).toEqual({ isDryRun: true })
    expect(normalise({ isDryRun: 'false' })).toEqual({ isDryRun: false })
    // Cleared select must drop the key entirely, not send `false`.
    expect(normalise({ isDryRun: '' })).toEqual({})
  })
})
