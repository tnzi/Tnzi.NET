// Avoid @vue-flow/core import chain (requires full bundler setup) by using a minimal type-only shim
vi.mock('@vue-flow/core', () => ({}))

import { describe, it, expect, vi } from 'vitest'
import { useWorkflowVisualization, type WorkflowDefinition } from '../../src/composables/useWorkflowVisualization'

describe('useWorkflowVisualization', () => {
  it('starts with empty nodes and edges', () => {
    const w = useWorkflowVisualization()
    expect(w.nodes.value).toEqual([])
    expect(w.edges.value).toEqual([])
  })

  it('setWorkflow converts node defs to VueFlow Node shape', () => {
    const w = useWorkflowVisualization()
    const def: WorkflowDefinition = {
      nodes: [
        { id: 'n1', type: 'start', label: 'Start', position: { x: 0, y: 0 } },
        { id: 'n2', type: 'task', label: 'Task', description: 'do work', status: 'active' },
      ],
      edges: [],
    }
    w.setWorkflow(def)
    expect(w.nodes.value).toHaveLength(2)
    expect(w.nodes.value[0]).toMatchObject({ id: 'n1', type: 'custom', label: 'Start', position: { x: 0, y: 0 } })
    expect(w.nodes.value[0]!.data).toMatchObject({ label: 'Start', status: 'pending', nodeType: 'start' })
    expect(w.nodes.value[1]!.data).toMatchObject({ description: 'do work', status: 'active' })
  })

  it('auto-places nodes without explicit position', () => {
    const w = useWorkflowVisualization()
    w.setWorkflow({
      nodes: [
        { id: 'a', type: 't', label: 'A' },
        { id: 'b', type: 't', label: 'B' },
        { id: 'c', type: 't', label: 'C' },
      ],
      edges: [],
    })
    expect(w.nodes.value[0]!.position).toEqual({ x: 0, y: 100 })
    expect(w.nodes.value[1]!.position).toEqual({ x: 250, y: 100 })
    expect(w.nodes.value[2]!.position).toEqual({ x: 500, y: 100 })
  })

  it('setWorkflow converts edge defs to VueFlow Edge shape', () => {
    const w = useWorkflowVisualization()
    w.setWorkflow({
      nodes: [{ id: 'a', type: 't', label: 'A' }, { id: 'b', type: 't', label: 'B' }],
      edges: [
        { id: 'e1', source: 'a', target: 'b', label: 'next', animated: true },
        { id: 'e2', source: 'a', target: 'b' },
      ],
    })
    expect(w.edges.value[0]).toMatchObject({ id: 'e1', source: 'a', target: 'b', label: 'next', type: 'animated' })
    expect(w.edges.value[0]!.data).toMatchObject({ variant: 'animated' })
    expect(w.edges.value[1]!.type).toBe('default')
  })

  it('updateNodeStatus mutates only the target node', () => {
    const w = useWorkflowVisualization()
    w.setWorkflow({
      nodes: [
        { id: 'a', type: 't', label: 'A' },
        { id: 'b', type: 't', label: 'B' },
      ],
      edges: [],
    })
    w.updateNodeStatus('b', 'completed')
    expect(w.nodes.value[0]!.data!.status).toBe('pending')
    expect(w.nodes.value[1]!.data!.status).toBe('completed')
  })

  it('reset empties nodes and edges', () => {
    const w = useWorkflowVisualization()
    w.setWorkflow({
      nodes: [{ id: 'a', type: 't', label: 'A' }],
      edges: [{ id: 'e', source: 'a', target: 'a' }],
    })
    w.reset()
    expect(w.nodes.value).toEqual([])
    expect(w.edges.value).toEqual([])
  })
})
