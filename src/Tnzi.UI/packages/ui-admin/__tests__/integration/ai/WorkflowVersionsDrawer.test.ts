import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Workflow version history: the drawer lists versions (GET versions), a row
 * opens one snapshot (GET versions/{n}) and restore posts
 * versions/{n}/restore - hidden without `ai.workflow.update`.
 */

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'wf-1' }, query: {} }),
  useRouter: () => ({ push: vi.fn(() => Promise.resolve()), replace: vi.fn(() => Promise.resolve()) }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const getVersions = vi.fn()
const getVersion = vi.fn()
const restoreVersion = vi.fn()

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflows: { getVersions, getVersion, restoreVersion },
  }),
}))

import WorkflowVersionsDrawer from '../../../src/pages/ai/workflows/WorkflowVersionsDrawer.vue'
import { summarizeVersionSnapshot } from '../../../src/pages/ai/workflows/workflow-versions'

// Overlays render inline (no teleport) and a popconfirm fires on its trigger.
const stubs = {
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div><slot name="header" /><slot /><slot name="footer" /></div>' },
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show" class="modal-stub"><slot name="header" /><slot /><slot name="footer" /></div>' },
  Popconfirm: {
    name: 'Popconfirm',
    template: '<div class="popconfirm-stub"><span class="popconfirm-trigger" @click="$emit(\'positive-click\')"><slot name="trigger" /></span><span class="popconfirm-text"><slot /></span></div>',
  },
}

const history = [
  { id: 'v2', workflowDefinitionId: 'wf-1', versionNumber: 2, changeDescription: 'Before restore to version 1', creationTime: '2026-09-02T00:00:00Z' },
  { id: 'v1', workflowDefinitionId: 'wf-1', versionNumber: 1, changeDescription: null, creationTime: '2026-09-01T00:00:00Z' },
]

const snapshotJson = JSON.stringify({
  name: 'Old name',
  description: null,
  steps: [{ stepId: 'a' }, { stepId: 'b' }],
  executionMode: 'Dag',
  isEnabled: false,
})

type DrawerVm = { open: () => void }

async function mountOpen(props: { canRestore: boolean; dirty?: boolean }) {
  const wrapper = mount(WorkflowVersionsDrawer, {
    props: { workflowId: 'wf-1', dirty: false, ...props },
    global: { stubs },
    attachTo: document.body,
  })
  ;(wrapper.vm as unknown as DrawerVm).open()
  await flushPromises()
  return wrapper
}

function rowButton(wrapper: ReturnType<typeof mount>, rowIndex: number, label: string) {
  const row = wrapper.findAll('tbody tr')[rowIndex]
  return row?.findAll('button').find((b) => b.text() === label)
}

describe('WorkflowVersionsDrawer', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
    getVersions.mockReset().mockResolvedValue(history)
    getVersion.mockReset().mockImplementation(async (_id: string, n: number) => ({ ...history.find((v) => v.versionNumber === n), definition: snapshotJson }))
    restoreVersion.mockReset().mockResolvedValue(undefined)
  })

  it('lists the history of the workflow when opened', async () => {
    const wrapper = await mountOpen({ canRestore: true })
    expect(getVersions).toHaveBeenCalledWith('wf-1')
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    expect(wrapper.text()).toContain('v2')
    expect(wrapper.text()).toContain('Before restore to version 1')
  })

  it('shows the refusal instead of an empty history when the read fails', async () => {
    getVersions.mockRejectedValueOnce(new Error('Workflow not found'))
    const wrapper = await mountOpen({ canRestore: true })
    const alert = wrapper.find('[data-test="wf-versions-error"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('Workflow not found')
    expect(wrapper.find('tbody').exists()).toBe(false)
  })

  it('views one version by fetching its snapshot, then restores it', async () => {
    const wrapper = await mountOpen({ canRestore: true })
    await rowButton(wrapper, 1, 'View')!.trigger('click')
    await flushPromises()

    expect(getVersion).toHaveBeenCalledWith('wf-1', 1)
    const snapshot = wrapper.find('[data-test="wf-version-snapshot"]')
    expect(snapshot.text()).toContain('Old name')
    expect(snapshot.text()).toContain('Dag')

    await wrapper.find('[data-test="wf-version-restore"]').trigger('click')
    await flushPromises()
    expect(restoreVersion).toHaveBeenCalledWith('wf-1', 1)
    expect(wrapper.emitted('restored')?.[0]).toEqual([1])
    // The history re-reads: the restore itself added a snapshot.
    expect(getVersions).toHaveBeenCalledTimes(2)
  })

  it('warns that a restore discards unsaved editor changes', async () => {
    const wrapper = await mountOpen({ canRestore: true, dirty: true })
    await rowButton(wrapper, 0, 'View')!.trigger('click')
    await flushPromises()
    const confirm = wrapper.find('.modal-stub .popconfirm-text')
    expect(confirm.text()).toContain('Restore version 2?')
    expect(confirm.text()).toContain('Unsaved changes in the editor will be discarded.')
  })

  it('does not report success when the restore is refused', async () => {
    restoreVersion.mockRejectedValueOnce(new Error('Workflow version snapshot is not restorable'))
    const wrapper = await mountOpen({ canRestore: true })
    await rowButton(wrapper, 0, 'Restore')!.trigger('click')
    await flushPromises()
    expect(restoreVersion).toHaveBeenCalledWith('wf-1', 2)
    expect(wrapper.emitted('restored')).toBeUndefined()
  })

  it('hides every restore action without ai.workflow.update', async () => {
    const wrapper = await mountOpen({ canRestore: false })
    expect(rowButton(wrapper, 0, 'Restore')).toBeUndefined()
    await rowButton(wrapper, 0, 'View')!.trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-test="wf-version-snapshot"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="wf-version-restore"]').exists()).toBe(false)
  })
})

describe('summarizeVersionSnapshot', () => {
  it('reads a camelCase snapshot with embedded steps', () => {
    const s = summarizeVersionSnapshot(snapshotJson)
    expect(s).toMatchObject({ parsed: true, name: 'Old name', executionMode: 'Dag', isEnabled: false, stepCount: 2 })
  })

  it('reads a PascalCase snapshot whose steps are a JSON string (older snapshots)', () => {
    const s = summarizeVersionSnapshot(JSON.stringify({ Name: 'Legacy', Steps: '[{"stepId":"x"}]', ExecutionMode: 'Sequential', IsEnabled: true }))
    expect(s).toMatchObject({ parsed: true, name: 'Legacy', stepCount: 1, isEnabled: true })
    expect(JSON.parse(s.pretty).Steps).toEqual([{ stepId: 'x' }])
  })

  it('reports an unreadable snapshot instead of an empty definition', () => {
    const s = summarizeVersionSnapshot('{not json')
    expect(s.parsed).toBe(false)
    expect(s.pretty).toBe('{not json')
    expect(s.stepCount).toBeUndefined()
  })
})
