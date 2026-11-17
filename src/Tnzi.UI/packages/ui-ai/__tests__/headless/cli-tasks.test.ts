import { describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import {
  collectTasks,
  findToolCall,
  findToolCallOwner,
  formatTaskDuration,
  isBackgroundCommand,
  isSubAgentCall,
  mergeTaskCalls,
  mergeTaskDetail,
  mergeToolCall,
  replyToolCalls,
  subAgentSteps,
  taskElapsedMs,
  taskStatus,
  taskTitle,
  taskToolCount,
  upsertToolCalls,
} from '../../src/headless/cli-tasks'
import { useNow } from '../../src/headless/useNow'
import type { ChatMessage, ToolCallInfo } from '../../src/headless/useChat'

const agent = (id: string, patch: Partial<ToolCallInfo> = {}, task: ToolCallInfo['task'] = {}): ToolCallInfo => ({
  id,
  name: 'Agent',
  status: 'completed',
  textOffset: 0,
  ...patch,
  task: { kind: 'agent', status: 'running', steps: [], ...task },
})

const command = (id: string, task: ToolCallInfo['task'] = {}, input: unknown = { command: 'pnpm test' }): ToolCallInfo => ({
  id,
  name: 'Bash',
  status: 'completed',
  textOffset: 0,
  input,
  task: { kind: 'command', background: true, status: 'running', ...task },
})

const call = (id: string, parentId?: string, patch: Partial<ToolCallInfo> = {}): ToolCallInfo => ({
  id,
  name: 'Read',
  status: 'completed',
  ...(parentId ? { parentId } : { textOffset: 0 }),
  ...patch,
})

const message = (id: string, toolCalls: ToolCallInfo[]): ChatMessage => ({
  id,
  role: 'assistant',
  content: '',
  createdAt: '2026-11-09T15:00:00Z',
  toolCalls,
})

describe('kinds of task', () => {
  it('tells a sub-agent from a background command and from a command run in place', () => {
    expect(isSubAgentCall(agent('a'))).toBe(true)
    expect(isSubAgentCall(command('b'))).toBe(false)
    expect(isBackgroundCommand(command('b'))).toBe(true)
    expect(isBackgroundCommand(command('c', { background: false }))).toBe(false)
    expect(isBackgroundCommand(call('d'))).toBe(false)
    // An entry stored before tasks had a kind is a sub-agent when it reads like one
    expect(isSubAgentCall({ ...call('e'), task: { agentType: 'Explore' } })).toBe(true)
  })

  it('normalises the status and names a task by its description, type or command', () => {
    expect(taskStatus({ status: 'completed' })).toBe('completed')
    expect(taskStatus({ status: 'something-new' })).toBe('running')
    expect(taskStatus(null)).toBe('running')
    expect(taskTitle(agent('a', {}, { description: ' Survey ', agentType: 'Explore' }))).toBe('Survey')
    expect(taskTitle(agent('a', {}, { agentType: 'Explore' }))).toBe('Explore')
    expect(taskTitle(command('b', {}, { command: 'pnpm build --watch' }))).toBe('pnpm build --watch')
    expect(taskTitle(call('c'))).toBe('')
  })
})

describe('a sub-agent’s own view', () => {
  it('keeps the calls a sub-agent made out of the reply, unless their dispatch is not in it', () => {
    const calls = [agent('a'), call('r1', 'a'), call('main'), call('orphan', 'elsewhere')]

    expect(replyToolCalls(calls).map((c) => c.id)).toEqual(['a', 'main', 'orphan'])
  })

  it('reads its steps in order: text, runs of calls, a nested sub-agent, and calls its steps do not list', () => {
    const outer = agent('a', {}, {
      steps: [
        { type: 'tool', toolCallId: 'r1' },
        { type: 'tool', toolCallId: 'r2' },
        { type: 'text', text: 'Notes.' },
        { type: 'tool', toolCallId: 'b' },
        { type: 'text', text: '  ' },
      ],
    })
    const calls = [outer, call('r1', 'a'), call('r2', 'a'), agent('b', { parentId: 'a', textOffset: null }), call('late', 'a')]

    const steps = subAgentSteps(outer, calls)

    expect(steps.map((s) => s.kind)).toEqual(['tools', 'text', 'sub-agent', 'tools'])
    expect(steps[0]).toMatchObject({ toolCalls: [{ id: 'r1' }, { id: 'r2' }] })
    expect(steps[2]).toMatchObject({ toolCall: { id: 'b' } })
    expect(steps[3]).toMatchObject({ toolCalls: [{ id: 'late' }] })
  })

  it('counts tool calls as reported, else the calls under it', () => {
    expect(taskToolCount(agent('a', {}, { toolUseCount: 5 }), [])).toBe(5)
    expect(taskToolCount(agent('a'), [call('r1', 'a'), call('r2', 'a'), call('x')])).toBe(2)
  })
})

describe('time', () => {
  it('counts a running task from its start and a finished one by what was reported', () => {
    const start = Date.parse('2026-11-09T15:00:00Z')
    expect(taskElapsedMs({ status: 'running', startedAt: '2026-11-09T15:00:00Z', durationMs: 2000 }, start + 18_000)).toBe(18_000)
    expect(taskElapsedMs({ status: 'running', startedAt: '2026-11-09T15:00:00Z', durationMs: 90_000 }, start + 65_000)).toBe(90_000)
    expect(taskElapsedMs({ status: 'completed', startedAt: '2026-11-09T15:00:00Z', durationMs: 4200 }, start + 999_999)).toBe(4200)
    expect(taskElapsedMs({ status: 'running' }, start)).toBeNull()
    expect(taskElapsedMs(null)).toBeNull()
  })

  it('says it short enough for the end of a row', () => {
    expect(formatTaskDuration(18_400)).toBe('18s')
    expect(formatTaskDuration(372_000)).toBe('6m')
    expect(formatTaskDuration(3_720_000)).toBe('1h 02m')
    expect(formatTaskDuration(null)).toBe('')
    expect(formatTaskDuration(-1)).toBe('')
  })

  it('ticks once a second while active, and stops', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-09T15:00:00Z'))
    const active = ref(true)
    const scope = effectScope()
    const now = scope.run(() => useNow(() => active.value))!

    vi.advanceTimersByTime(3_000)
    expect(now.value).toBe(Date.parse('2026-11-09T15:00:03Z'))

    active.value = false
    await nextTick()
    vi.advanceTimersByTime(5_000)
    expect(now.value).toBe(Date.parse('2026-11-09T15:00:03Z'))
    scope.stop()
    vi.useRealTimers()
  })
})

describe('merging reports', () => {
  it('lets values win, extends steps, and keeps an outcome', () => {
    const older = { status: 'completed', description: 'Survey', steps: [{ type: 'tool', toolCallId: 'r1' }] }

    expect(mergeTaskDetail(older, { status: 'running', activity: 'late', steps: [{ type: 'text', text: 'More.' }] })).toEqual({
      status: 'completed',
      description: 'Survey',
      activity: 'late',
      steps: [{ type: 'tool', toolCallId: 'r1' }, { type: 'text', text: 'More.' }],
    })
    expect(mergeTaskDetail({ status: 'running', steps: [{ type: 'tool', toolCallId: 'r1' }] }, {
      status: 'completed',
      model: 'claude-haiku-5-5',
      steps: [{ type: 'tool', toolCallId: 'r1' }, { type: 'tool', toolCallId: 'r2' }],
    })).toMatchObject({ status: 'completed', model: 'claude-haiku-5-5', steps: [{ toolCallId: 'r1' }, { toolCallId: 'r2' }] })
    expect(mergeTaskDetail(null, { status: 'running' })).toEqual({ status: 'running' })
    expect(mergeTaskDetail({ status: 'running' }, null)).toEqual({ status: 'running' })
  })

  it('lets a stand-in update bring only the task’s state', () => {
    const real = agent('a', { input: { prompt: 'x' }, textOffset: 12 })
    const standIn: ToolCallInfo = { id: 'a', name: '', status: 'running', textOffset: 0, task: { status: 'completed', result: 'Done.' } }

    const merged = mergeToolCall(real, standIn)

    expect(merged).toMatchObject({ name: 'Agent', status: 'completed', textOffset: 12, input: { prompt: 'x' } })
    expect(merged.task).toMatchObject({ status: 'completed', result: 'Done.', kind: 'agent' })
    expect(upsertToolCalls([real], [call('r1', 'a')]).map((c) => c.id)).toEqual(['a', 'r1'])
  })

  it('finds the message an update belongs to, by its id or its dispatch', () => {
    const messages = [message('m1', [agent('a'), call('r1', 'a')]), message('m2', [call('x')])]

    expect(findToolCallOwner(messages, call('r2', 'a'), 'm2')?.id).toBe('m1')
    expect(findToolCallOwner(messages, agent('a'), 'm2')?.id).toBe('m1')
    expect(findToolCallOwner(messages, call('y'), 'm2')).toBeNull()
    expect(findToolCallOwner(messages, call('x'), 'm2')).toBeNull()
    expect(findToolCall(messages, 'r1')).toMatchObject({ message: { id: 'm1' }, toolCall: { id: 'r1' } })
    expect(findToolCall(messages, 'nope')).toBeNull()
  })

  it('folds a stand-in from a later message into the call where the task was started', () => {
    const standIn: ToolCallInfo = {
      id: 'a', name: '', status: 'running', textOffset: 3,
      task: { status: 'completed', result: 'Report.', steps: [{ type: 'tool', toolCallId: 'r2' }] },
    }
    const messages = [
      message('m1', [agent('a', {}, { background: true, steps: [{ type: 'tool', toolCallId: 'r1' }] }), call('r1', 'a')]),
      message('m2', [call('main'), standIn, call('r2', 'a')]),
    ]

    const merged = mergeTaskCalls(messages)

    expect(merged[1]!.toolCalls!.map((c) => c.id)).toEqual(['main'])
    expect(merged[0]!.toolCalls!.map((c) => c.id)).toEqual(['a', 'r1', 'r2'])
    expect(merged[0]!.toolCalls![0]!.task).toMatchObject({
      status: 'completed', result: 'Report.', background: true,
      steps: [{ toolCallId: 'r1' }, { toolCallId: 'r2' }],
    })
    expect(mergeTaskCalls([messages[1]!])[0]).toBe(messages[1])
  })
})

describe('a conversation’s tasks', () => {
  it('lists sub-agents and background commands in order, nested ones after the one that dispatched them', () => {
    const messages = [
      message('m1', [
        command('c1', { status: 'completed' }),
        agent('a', {}, { description: 'Outer', status: 'completed' }),
        agent('b', { parentId: 'a' }, { description: 'Inner' }),
        call('r', 'a'),
        command('fg', { background: false }),
      ]),
      message('m2', [agent('c', {}, { description: 'Later', status: 'failed' })]),
    ]

    expect(collectTasks(messages).map((e) => [e.id, e.messageId, e.kind, e.depth, e.status])).toEqual([
      ['c1', 'm1', 'command', 0, 'completed'],
      ['a', 'm1', 'agent', 0, 'completed'],
      ['b', 'm1', 'agent', 1, 'running'],
      ['c', 'm2', 'agent', 0, 'failed'],
    ])
  })
})
