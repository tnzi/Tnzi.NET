import { describe, it, expect } from 'vitest'
import { removeStepFromGraph, renameStepInGraph, sameStepId } from '../../../src/pages/ai/workflows/workflow-graph-refs'

const config = () => ({
  conditionalEdges: [
    { fromNodeId: 'route', routes: { a: 'Draft', b: 'publish' }, defaultTarget: 'draft', conditionType: 'OutputEquals' },
  ],
  loops: { polish: { nodeIds: ['draft', 'review'], maxIterations: 4 } },
})

describe('workflow graph references', () => {
  it('compares step ids ignoring case, as the backend does', () => {
    expect(sameStepId('Step-1', 'step-1')).toBe(true)
    expect(sameStepId('step-1', 'step-10')).toBe(false)
    expect(sameStepId(null, 'step-1')).toBe(false)
  })

  it('renames every reference, whatever its casing, and keeps the rest', () => {
    const input = config()
    const { configuration, changed } = renameStepInGraph(input, 'draft', 'compose')
    expect(changed).toBe(true)
    expect(configuration).toEqual({
      conditionalEdges: [
        { fromNodeId: 'route', routes: { a: 'compose', b: 'publish' }, defaultTarget: 'compose', conditionType: 'OutputEquals' },
      ],
      loops: { polish: { nodeIds: ['compose', 'review'], maxIterations: 4 } },
    })
    // The caller's object is never touched.
    expect(input).toEqual(config())
  })

  it('reports no change when the step is not referenced', () => {
    expect(renameStepInGraph(config(), 'unrelated', 'x').changed).toBe(false)
    expect(removeStepFromGraph(config(), 'unrelated').changed).toBe(false)
    expect(renameStepInGraph(null, 'a', 'b')).toEqual({ configuration: null, changed: false })
  })

  it('removes a deleted step from routes, default targets and loops', () => {
    const input = config()
    const { configuration, changed } = removeStepFromGraph(input, 'DRAFT')
    expect(changed).toBe(true)
    expect(configuration).toEqual({
      conditionalEdges: [{ fromNodeId: 'route', routes: { b: 'publish' }, defaultTarget: null, conditionType: 'OutputEquals' }],
      loops: { polish: { nodeIds: ['review'], maxIterations: 4 } },
    })
    expect(input).toEqual(config())
  })

  it('drops an edge leaving the deleted step and a loop left with no steps', () => {
    const { configuration } = removeStepFromGraph(
      { conditionalEdges: [{ fromNodeId: 'route', routes: { a: 'x' } }], loops: { only: { nodeIds: ['route'] } } },
      'route',
    )
    expect(configuration).toEqual({ conditionalEdges: [], loops: {} })
  })
})
