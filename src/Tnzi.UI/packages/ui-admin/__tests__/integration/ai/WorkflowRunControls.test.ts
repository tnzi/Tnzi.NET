import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

/**
 * Run viewer execution controls: selection by execution id, cancel, the
 * pending-interrupt panel (resume-with-input) and the pending-signals list.
 * Every write is gated on `ai.workflow.execute`, the code the backend enforces.
 */

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), back: vi.fn() }),
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const summaries = [
  // `id` (row key) and `executionId` (what every execution route takes) differ on purpose.
  { id: 'row-1', executionId: 'exec-1', workflowDefinitionId: 'wf-1', status: 'Running', completedStepCount: 1, awaitingApprovalCount: 0, creationTime: '2026-09-01T00:00:00Z', updatedTime: '2026-09-01T00:00:00Z' },
]

let detailOverrides: Record<string, unknown> = {}
const getDetail = vi.fn(async (executionId: string) => ({
  id: 'row-1',
  executionId,
  workflowDefinitionId: 'wf-1',
  status: 'Running',
  completedStepCount: 1,
  awaitingApprovalCount: 0,
  creationTime: '2026-09-01T00:00:00Z',
  updatedTime: '2026-09-01T00:00:00Z',
  initialInput: 'hi',
  completedStepIds: ['a'],
  stepsAwaitingApproval: [],
  stepOutputs: { a: 'out' },
  currentWaitReason: null,
  ...detailOverrides,
}))
const cancel = vi.fn()
const getInterrupt = vi.fn()
const resumeWithInput = vi.fn()
const getSignals = vi.fn()

vi.mock('../../../src/services/bridges/ai-bridge', () => ({
  createAiBridge: () => ({
    workflows: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 100 })),
    },
    workflowRuns: {
      fetch: vi.fn(async () => ({ items: summaries, totalCount: 1, pageIndex: 1, pageSize: 20 })),
      getDetail,
      resume: vi.fn(),
      approveStep: vi.fn(),
      rejectStep: vi.fn(),
      cancel,
      getInterrupt,
      resumeWithInput,
      getSignals,
    },
  }),
}))

import WorkflowRunViewer from '../../../src/pages/ai/workflows/WorkflowRunViewer.vue'

const stubs = {
  Popconfirm: {
    name: 'Popconfirm',
    template: '<div class="popconfirm-stub"><span class="popconfirm-trigger" @click="$emit(\'positive-click\')"><slot name="trigger" /></span><span class="popconfirm-text"><slot /></span></div>',
  },
}

async function mountAndSelect() {
  const wrapper = mount(WorkflowRunViewer, { global: { stubs } })
  await flushPromises()
  await wrapper.find('.t-wf-run-page__run-item').trigger('click')
  await flushPromises()
  return wrapper
}

function grant(...permissions: string[]): void {
  useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions } as never)
}

describe('WorkflowRunViewer execution controls', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    detailOverrides = {}
    getDetail.mockClear()
    cancel.mockReset().mockResolvedValue(undefined)
    getInterrupt.mockReset().mockResolvedValue({
      executionId: 'exec-1',
      stepId: 'gate',
      type: 'Approval',
      reason: 'Approval required for step gate',
      requestedInput: { approved: 'bool: whether to approve', comment: 'string: optional comment' },
      timeoutSeconds: null,
    })
    resumeWithInput.mockReset().mockResolvedValue({ status: 'Completed', output: 'done' })
    getSignals.mockReset().mockResolvedValue([])
  })

  it('selects a run by its execution id, not the row id', async () => {
    await mountAndSelect()
    expect(getDetail).toHaveBeenCalledWith('exec-1')
    expect(getDetail).not.toHaveBeenCalledWith('row-1')
  })

  it('cancels a running execution through the execution id', async () => {
    const wrapper = await mountAndSelect()
    const button = wrapper.find('[data-test="wf-run-cancel"]')
    expect(button.exists()).toBe(true)
    // A running execution stops at its next checkpoint - the confirm says so.
    expect(wrapper.find('.t-wf-run-page__detail-header .popconfirm-text').text()).toContain('next checkpoint')
    await button.trigger('click')
    await flushPromises()
    expect(cancel).toHaveBeenCalledWith('exec-1')
    expect(getDetail).toHaveBeenCalledTimes(2)
  })

  it('shows a queued cancel instead of offering cancel again', async () => {
    detailOverrides = { currentWaitReason: 'cancel_requested' }
    const wrapper = await mountAndSelect()
    expect(wrapper.find('[data-test="wf-run-cancel-requested"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="wf-run-cancel"]').exists()).toBe(false)
  })

  it('offers no cancel on a finished run and does not ask for its signals', async () => {
    detailOverrides = { status: 'Completed' }
    const wrapper = await mountAndSelect()
    expect(wrapper.find('[data-test="wf-run-cancel"]').exists()).toBe(false)
    expect(getSignals).not.toHaveBeenCalled()
  })

  it('hides cancel and the interrupt answer form without ai.workflow.execute', async () => {
    grant('ai.workflow.view')
    detailOverrides = { status: 'AwaitingInput' }
    const wrapper = await mountAndSelect()
    expect(wrapper.find('[data-test="wf-run-cancel"]').exists()).toBe(false)
    // The interrupt itself is a read (class-level ai.workflow.view) and still shows.
    expect(wrapper.find('[data-test="wf-interrupt"]').text()).toContain('Approval required for step gate')
    expect(wrapper.find('[data-test="wf-interrupt-submit"]').exists()).toBe(false)
  })

  it('lists pending signals, and shows the refusal when the mailbox read fails', async () => {
    getSignals.mockResolvedValueOnce([
      { signalId: 's1', type: 'cancel', reason: 'Cancelled by operator', createdAt: '2026-09-01T00:01:00Z' },
    ])
    const wrapper = await mountAndSelect()
    expect(getSignals).toHaveBeenCalledWith('exec-1')
    expect(wrapper.find('[data-test="wf-run-signals"]').text()).toContain('Cancelled by operator')

    getSignals.mockRejectedValueOnce(new Error('Workflow mailbox is not available'))
    await wrapper.find('.t-wf-run-page__run-item').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-test="wf-run-signals-error"]').text()).toContain('Workflow mailbox is not available')
    expect(wrapper.find('[data-test="wf-run-signals-empty"]').exists()).toBe(false)
  })

  describe('pending interrupt (AwaitingInput)', () => {
    beforeEach(() => {
      detailOverrides = { status: 'AwaitingInput' }
    })

    it('loads the interrupt and resumes with the answer for the waiting step', async () => {
      const wrapper = await mountAndSelect()
      expect(getInterrupt).toHaveBeenCalledWith('exec-1')
      const panel = wrapper.find('[data-test="wf-interrupt"]')
      expect(panel.text()).toContain('gate')
      expect(panel.text()).toContain('bool: whether to approve')

      // The answer is prefilled from the requested fields.
      const textarea = wrapper.find('[data-test="wf-interrupt-input"] textarea')
      expect(JSON.parse((textarea.element as HTMLTextAreaElement).value)).toEqual({ approved: false, comment: '' })

      await textarea.setValue('{"approved": true, "comment": "ok"}')
      await wrapper.find('[data-test="wf-interrupt-submit"]').trigger('click')
      await flushPromises()
      expect(resumeWithInput).toHaveBeenCalledWith('exec-1', 'gate', { approved: true, comment: 'ok' })
      // The run is re-read after the resume.
      expect(getDetail).toHaveBeenCalledTimes(2)
    })

    it('rejects an answer that is not a JSON object', async () => {
      const wrapper = await mountAndSelect()
      await wrapper.find('[data-test="wf-interrupt-input"] textarea').setValue('[1, 2]')
      await wrapper.find('[data-test="wf-interrupt-submit"]').trigger('click')
      await flushPromises()
      expect(resumeWithInput).not.toHaveBeenCalled()
      expect(wrapper.find('[data-test="wf-interrupt-answer-error"]').exists()).toBe(true)
    })

    it('shows the refusal when the interrupt cannot be read', async () => {
      getInterrupt.mockRejectedValueOnce(new Error('No pending interrupt for this execution'))
      const wrapper = await mountAndSelect()
      expect(wrapper.find('[data-test="wf-interrupt-error"]').text()).toContain('No pending interrupt')
      expect(wrapper.find('[data-test="wf-interrupt-submit"]').exists()).toBe(false)
    })
  })
})
