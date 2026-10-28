/**
 * `useWorkflowVisualization` and `TWorkflowCanvas` ship side by side from
 * `@tnzi/ui-ai/workflow`. The hook emits node type `custom` (status-bearing
 * cards) and edge type `animated`; the canvas used to register neither, so
 * vue-flow logged "type is missing" and fell back to bare default nodes: the
 * status the hook exists to show never reached the screen, and an animated
 * edge was drawn as a plain one.
 */
import { afterEach, describe, it, expect, vi } from 'vitest'
import { createApp, h, nextTick, type App } from 'vue'
import TWorkflowCanvas from '../../../src/components/workflow/TWorkflowCanvas.vue'
import { useWorkflowVisualization } from '../../../src/headless/useWorkflowVisualization'

let app: App | null = null
let host: HTMLElement | null = null

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  document.body.innerHTML = ''
  vi.restoreAllMocks()
})

async function mountCanvas(slots?: Record<string, (props: Record<string, unknown>) => unknown>): Promise<HTMLElement> {
  const w = useWorkflowVisualization()
  w.setWorkflow({
    nodes: [
      { id: 'a', type: 'start', label: 'Start node', status: 'completed', position: { x: 0, y: 0 } },
      { id: 'b', type: 'task', label: 'Task node', description: 'does work', status: 'active', position: { x: 300, y: 0 } },
    ],
    edges: [{ id: 'e1', source: 'a', target: 'b', animated: true }],
  })
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({
    render: () => h(TWorkflowCanvas, { nodes: w.nodes.value as never, edges: w.edges.value as never }, slots),
  })
  app.mount(host)
  await nextTick()
  await new Promise((resolve) => setTimeout(resolve, 0))
  return host
}

describe('TWorkflowCanvas with useWorkflowVisualization', () => {
  it('renders the hook\'s custom nodes as status cards', async () => {
    const warn = vi.spyOn(console, 'warn')
    const el = await mountCanvas()

    expect(el.querySelectorAll('.vue-flow__node-custom')).toHaveLength(2)
    expect(el.querySelector('[data-id="b"] .t-wf-node--active')).not.toBeNull()
    expect(el.querySelector('[data-id="a"] .t-wf-node--completed')).not.toBeNull()
    expect(el.querySelector('[data-id="b"]')!.textContent).toContain('does work')
    const missing = warn.mock.calls.filter((call) => String(call[0]).includes('type is missing'))
    expect(missing).toEqual([])
  })

  it('draws the hook\'s animated edges with the flowing dot', async () => {
    const el = await mountCanvas()
    expect(el.querySelector('[data-id="e1"] animateMotion')).not.toBeNull()
  })

  it('lets a consumer slot replace the built-in node renderer', async () => {
    const el = await mountCanvas({
      'node-custom': (props) => h('span', { class: 'own-node' }, String((props.data as { label: string }).label)),
    })
    expect(el.querySelectorAll('.own-node')).toHaveLength(2)
    expect(el.querySelector('.t-wf-node')).toBeNull()
  })
})
