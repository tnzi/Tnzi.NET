import { describe, it, expect } from 'vitest'
import {
  NODE_TYPE_KEY,
  LEGACY_NODE_TYPE_KEY,
  getStepKind,
  normalizeStepConfiguration,
  workflowNodeTypes,
} from '../../../src/pages/ai/workflows/workflow-node-types'

/**
 * The node kind is a backend runtime contract: `WorkflowNodeExecutor` resolves
 * the `IWorkflowNode` from `configuration["nodeType"]` and silently falls back
 * to the agent node when the key is absent. Until 2026-09-12 the editor wrote
 * `__nodeType`, which no backend commit ever read, so every router / parallel /
 * debate / ... step built in the editor executed as a plain agent call with a
 * `Completed` status. These tests pin the key and the load-time migration.
 */
describe('workflow-node-types: node kind key contract', () => {
  it('writes the kind under the key the backend executor resolves', () => {
    expect(NODE_TYPE_KEY).toBe('nodeType')
    expect(LEGACY_NODE_TYPE_KEY).toBe('__nodeType')
  })

  it('every catalog kind is a lowercase backend WorkflowNodeTypes constant', () => {
    const backendConstants = [
      'agent', 'review', 'approval', 'router', 'parallel',
      'synthesize', 'debate', 'conditional', 'transform',
    ]
    for (const meta of workflowNodeTypes) {
      expect(backendConstants).toContain(meta.kind)
    }
  })

  describe('normalizeStepConfiguration', () => {
    it('migrates a legacy __nodeType into nodeType and drops the legacy key', () => {
      const out = normalizeStepConfiguration({ __nodeType: 'router', __x: '10', __y: '20' })
      expect(out).toEqual({ nodeType: 'router', __x: '10', __y: '20' })
      expect(out).not.toHaveProperty(LEGACY_NODE_TYPE_KEY)
    })

    it('keeps an explicit nodeType when both keys are present', () => {
      const out = normalizeStepConfiguration({ nodeType: 'parallel', __nodeType: 'router' })
      expect(out).toEqual({ nodeType: 'parallel' })
    })

    it('leaves a modern configuration untouched and never mutates the input', () => {
      const input = { nodeType: 'debate', temperature: '0.2' }
      const out = normalizeStepConfiguration(input)
      expect(out).toEqual(input)
      expect(out).not.toBe(input)
      expect(input).toEqual({ nodeType: 'debate', temperature: '0.2' })
    })

    it('returns an empty bag for a missing configuration', () => {
      expect(normalizeStepConfiguration(null)).toEqual({})
      expect(normalizeStepConfiguration(undefined)).toEqual({})
    })
  })

  describe('getStepKind', () => {
    it('reads the kind from nodeType', () => {
      expect(getStepKind({ configuration: { nodeType: 'router' } })).toBe('router')
      expect(getStepKind({ configuration: { nodeType: 'Approval' } })).toBe('approval')
    })

    it('does NOT read the legacy key: an unmigrated step must look like what it runs as', () => {
      expect(getStepKind({ configuration: { __nodeType: 'router' } })).toBe('agent')
    })

    it('falls back to agent for absent or unknown kinds, matching the executor', () => {
      expect(getStepKind({ configuration: {} })).toBe('agent')
      expect(getStepKind({ configuration: { nodeType: 'no-such-node' } })).toBe('agent')
      expect(getStepKind(null)).toBe('agent')
    })
  })
})
