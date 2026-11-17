import { describe, expect, it } from 'vitest'
import { callLineChanges, lineDiff, runImages, runLineChanges } from '../../src/components/reasoning/run-extras'
import { taskStepDescription, taskStepState } from '../../src/components/reasoning/task-steps'
import { summarizeToolRun, type ToolRunStep } from '../../src/components/reasoning/tool-steps'
import { describeToolStep } from '../../src/components/reasoning/tool-steps'
import { resolveToolEntry } from '../../src/components/reasoning/built-in-tool-renderers'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'
import type { ToolCallInfo } from '../../src/headless/useChat'

const edit = (oldText: string, newText: string, patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id: 'e', name: 'Edit', status: 'completed', input: { file_path: '/w/a.ts', old_string: oldText, new_string: newText }, ...patch,
})

describe('line counts', () => {
  it('counts lines added and removed between two texts', () => {
    expect(lineDiff('a\nb\nc', 'a\nx\nc\nd')).toEqual({ added: 2, removed: 1 })
    expect(lineDiff('', 'one\ntwo\n')).toEqual({ added: 2, removed: 0 })
    expect(lineDiff('same', 'same')).toEqual({ added: 0, removed: 0 })
    expect(lineDiff('x\r\ny', 'x\ny')).toEqual({ added: 0, removed: 0 })
  })

  it('reads Edit, MultiEdit and Write, and only completed calls', () => {
    expect(callLineChanges(edit('a', 'a\nb'))).toEqual({ added: 1, removed: 0 })
    expect(callLineChanges({ id: 'm', name: 'MultiEdit', status: 'completed', input: { edits: [{ old_string: 'a', new_string: 'b' }, { old_string: '', new_string: 'c\nd' }] } }))
      .toEqual({ added: 3, removed: 1 })
    expect(callLineChanges({ id: 'w', name: 'Write', status: 'completed', input: { content: '1\n2\n3\n' } })).toEqual({ added: 3, removed: 0 })
    expect(callLineChanges(edit('a', 'b', { status: 'failed' }))).toEqual({ added: 0, removed: 0 })
    expect(callLineChanges(edit('a', 'b', { status: 'running' }))).toEqual({ added: 0, removed: 0 })
    expect(callLineChanges({ id: 'r', name: 'Read', status: 'completed', input: { file_path: '/w/a.ts' } })).toEqual({ added: 0, removed: 0 })
    expect(runLineChanges([edit('a', 'b'), edit('x', 'x\ny')])).toEqual({ added: 2, removed: 1 })
  })
})

describe('images of a run', () => {
  it('are the stored image blocks of the steps’ results, with the step they came from', () => {
    const calls: ToolCallInfo[] = [
      { id: 'r1', name: 'Read', status: 'completed', result: [{ type: 'text', text: 'x' }, { type: 'image', mediaType: 'image/png', attachmentId: 'img-1' }] },
      { id: 's', name: 'mcp__tnzi__screenshot', status: 'completed', result: [{ type: 'image', attachmentId: 'img-2' }, { type: 'image' }] },
    ]

    expect(runImages(calls)).toEqual([
      { attachmentId: 'img-1', mediaType: 'image/png', toolCallId: 'r1', source: 'Read' },
      { attachmentId: 'img-2', mediaType: null, toolCallId: 's', source: 'Screenshot' },
    ])
    // The run names each step in its own words; a step it has none for keeps the tool's name
    expect(runImages(calls, (call) => (call.id === 'r1' ? '读取了文件' : '')).map((image) => image.source)).toEqual([
      '读取了文件',
      'Screenshot',
    ])
  })
})

describe('a run line that counts tasks', () => {
  const bg = (id: string, status: string): ToolCallInfo => ({
    id, name: 'Bash', status: 'completed', input: { command: 'pnpm test', run_in_background: true },
    task: { kind: 'command', background: true, status },
  })
  const agent = (id: string, status: string): ToolCallInfo => ({ id, name: 'Agent', status: 'completed', task: { kind: 'agent', status } })
  const read = (id: string): ToolCallInfo => ({ id, name: 'Read', status: 'completed', input: { file_path: '/w/a.ts' } })
  const failed: ToolCallInfo = { id: 'f', name: 'mcp__tnzi__screenshot', status: 'failed', error: 'offline' }

  const steps = (calls: ToolCallInfo[], t = en): ToolRunStep[] =>
    calls.map((c) => (c.task
      ? { description: taskStepDescription(c, t), state: taskStepState(c) }
      : { description: describeToolStep(c, resolveToolEntry(c.name, {}).describe, t), state: c.status === 'failed' ? 'failed' : 'completed' }))

  it('says what happened to the tasks, in natural counts', () => {
    const calls = [bg('b1', 'completed'), bg('b2', 'completed'), bg('b3', 'completed'), read('r1'), read('r2'), agent('a1', 'running'), failed]

    expect(summarizeToolRun(steps(calls), en).text).toBe(
      'Finished 3 background commands, read 2 files, an agent running, screenshot failed',
    )
  })

  it('says ongoing and stopped tasks apart from the finished ones, and in the interface language', () => {
    const calls = [bg('b1', 'running'), bg('b2', 'running'), agent('a1', 'stopped'), agent('a2', 'completed')]

    expect(summarizeToolRun(steps(calls), en).text).toBe('2 background commands running, stopped an agent, ran an agent')
    expect(summarizeToolRun(steps(calls, zhCn), zhCn).text).toBe('2 条后台命令在运行，停止了 1 个子 agent，运行了 1 个子 agent')
  })

  it('reads the CLI’s file and shell tools as what they did', () => {
    const calls: ToolCallInfo[] = [
      read('r1'),
      { id: 'c', name: 'Bash', status: 'completed', input: { command: 'git status' } },
      { id: 'e', name: 'Edit', status: 'completed', input: { file_path: '/w/a.ts' } },
      { id: 'g', name: 'Grep', status: 'completed', input: { pattern: 'x' } },
    ]

    expect(summarizeToolRun(steps(calls), en).text).toBe('Read a file, ran a command, edited a file, 1 more')
  })
})
