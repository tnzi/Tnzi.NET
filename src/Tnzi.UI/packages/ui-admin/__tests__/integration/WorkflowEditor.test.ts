import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * Phase 5 Task 5.5 — WorkflowEditor lazy-load shell test.
 *
 * @tnzi/ui-ai is mocked so the dynamic import resolves to a lightweight
 * stub component. The shell's job is to forward route :id as a prop to
 * the canvas — verified via the data attribute on the stub.
 */

vi.mock('@tnzi/ui-ai', () => ({
  WorkflowCanvas: {
    name: 'WorkflowCanvas',
    props: ['workflowId'],
    template: '<div data-test="canvas-stub" :data-workflow-id="workflowId">canvas</div>',
  },
}))

let routeParams: Record<string, unknown> = { id: 'wf-1' }
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams }),
}))

import WorkflowEditor from '../../src/pages/ai/workflows/WorkflowEditor.vue'

describe('WorkflowEditor page (Phase 5 Task 5.5 lazy-load shell)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    routeParams = { id: 'wf-1' }
  })

  it('mounts and lazy-loads WorkflowCanvas, forwarding route :id as workflowId', async () => {
    const wrapper = mount(WorkflowEditor)
    // Suspense + async component requires multiple microtask flushes
    await flushPromises()
    await flushPromises()

    const stub = wrapper.find('[data-test="canvas-stub"]')
    expect(stub.exists()).toBe(true)
    expect(stub.attributes('data-workflow-id')).toBe('wf-1')
  })

  it('renders empty state when route has no id', async () => {
    routeParams = {}
    const wrapper = mount(WorkflowEditor)
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('[data-test="canvas-stub"]').exists()).toBe(false)
    // Phase 5.16 wired the en locale: the page namespace 'ai.workflows'
    // resolves `editor.missingId` to its English label.
    expect(wrapper.text()).toContain('Select a workflow to edit.')
  })
})
