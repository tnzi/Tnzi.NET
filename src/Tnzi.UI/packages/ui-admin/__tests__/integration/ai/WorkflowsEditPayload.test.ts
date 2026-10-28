/**
 * The workflow list's Edit modal sends the fields it shows.
 *
 * It edits name, description, execution mode and the enabled switch, but it
 * PUT the whole list row, steps and graph configuration included, so steps
 * changed in the graph editor after the list loaded were reverted by a rename.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const workflowUpdate = vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) }))

function emptyAnswer() {
  return Object.assign([] as unknown[], { items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })
}

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/ai-bridge', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../src/services/bridges/ai-bridge')>()
  const section = (overrides: Record<string, unknown> = {}) =>
    new Proxy(overrides, { get: (target, key) => target[key as string] ?? vi.fn(async () => emptyAnswer()) })
  return {
    ...original,
    createAiBridge: () =>
      new Proxy({} as Record<string, unknown>, {
        get: (_t, key) => (key === 'workflows' ? section({ update: workflowUpdate }) : section()),
      }),
  }
})
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({
    push: vi.fn(async () => undefined),
    replace: vi.fn(async () => undefined),
    resolve: (to: { name: string }) => ({ path: `/admin/${String(to.name)}` }),
  }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

import Workflows from '../../../src/pages/ai/workflows/Workflows.vue'

describe('Workflows list Edit modal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    workflowUpdate.mockClear()
  })

  it('sends name, description, execution mode and enabled, not the steps or the graph', async () => {
    const wrapper = mount(Workflows)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      crud: {
        openEdit: (row: unknown) => void
        submit: () => Promise<unknown>
        formModal: { formData: { value: Record<string, unknown> } }
      }
    }
    vm.crud.openEdit({
      id: 'wf-1', name: 'Intake', description: 'Old', executionMode: 'Dag', isEnabled: true,
      steps: [{ stepId: 'stale', order: 0, maxRetries: 0, retryDelaySeconds: 0, requiresApproval: false }],
      configuration: { edges: [], loops: [] }, creationTime: '2026-04-01T00:00:00Z',
    })
    vm.crud.formModal.formData.value.name = 'Intake v2'
    await vm.crud.submit()

    expect(workflowUpdate).toHaveBeenCalledTimes(1)
    const dto = workflowUpdate.mock.calls[0]![1] as Record<string, unknown>
    expect(dto).toEqual({ name: 'Intake v2', description: 'Old', executionMode: 'Dag', isEnabled: true })
  })
})
