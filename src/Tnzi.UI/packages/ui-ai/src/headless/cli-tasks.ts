/**
 * Tasks of an external CLI agent, as the conversation shows them: the sub-agents it dispatched (claude's `Agent`
 * tool) and the commands it runs in the background.
 *
 * The backend reports a task on the tool call that started it (`ToolCallInfo.task`): its kind, description, state,
 * usage and report, and for a sub-agent its steps in order. The calls a sub-agent made are entries of the same reply's
 * tool-call list with `parentId` set to the dispatch's `id`; a sub-agent it dispatched in turn is one of those, with a
 * `task` of its own. The functions here read that shape: which calls belong to the reply and which to a sub-agent's
 * own view, what that view shows step by step, how long a task has run, and every task of a conversation for a list.
 *
 * A background task can outlive the answer that started it: its later progress arrives while the run streams the next
 * answer, or, when the next message took the run's process over, in another run whose reply carries a stand-in entry
 * for it (same `id`, no `name`). Updates are routed to the message that holds the call ({@link findToolCallOwner}) and
 * stand-ins are folded into it ({@link mergeTaskCalls}), so a task is always where the agent started it.
 */
import type { ToolTaskDetailDto, ToolTaskStepDto } from '@tnzi/core/services/ai'
import type { ChatMessage, ToolCallInfo } from './useChat'

/** Where a task stands. Anything the backend reports that is not a known outcome reads as running. */
export type TaskStatus = 'running' | 'completed' | 'failed' | 'stopped'

/** A task's state, normalised. */
export function taskStatus(detail: ToolTaskDetailDto | null | undefined): TaskStatus {
  switch (detail?.status) {
    case 'completed':
      return 'completed'
    case 'failed':
      return 'failed'
    case 'stopped':
      return 'stopped'
    default:
      return 'running'
  }
}

/** Whether a call dispatched a sub-agent. */
export function isSubAgentCall(call: ToolCallInfo): boolean {
  return !!call.task && (call.task.kind === 'agent' || (!call.task.kind && !!(call.task.agentType || call.task.steps?.length)))
}

/** Whether a call started a command that runs in the background. */
export function isBackgroundCommand(call: ToolCallInfo): boolean {
  return call.task?.kind === 'command' && call.task.background === true
}

/** Whether a call started a task the conversation lists apart (a sub-agent, a background command). */
export function isListedTask(call: ToolCallInfo): boolean {
  return isSubAgentCall(call) || isBackgroundCommand(call)
}

/**
 * The calls a reply shows itself: the main agent's, sub-agent dispatches included. A call made inside a sub-agent
 * whose dispatch is in the same list belongs to that sub-agent's view; one whose dispatch is not (it lives in an
 * earlier message, or never arrived) stays in the reply, so nothing the agent did disappears.
 */
export function replyToolCalls(toolCalls: readonly ToolCallInfo[] | null | undefined): ToolCallInfo[] {
  const calls = toolCalls ?? []
  const ids = new Set(calls.map((c) => c.id).filter((id): id is string => !!id))
  return calls.filter((c) => !c.parentId || !ids.has(c.parentId))
}

/** One step of a sub-agent as its view shows it. */
export type SubAgentStepView =
  | { readonly kind: 'text'; readonly text: string }
  /** Tool calls made back to back, shown as one run (`TToolRun`). */
  | { readonly kind: 'tools'; readonly toolCalls: readonly ToolCallInfo[] }
  /** A sub-agent this one dispatched. */
  | { readonly kind: 'sub-agent'; readonly toolCall: ToolCallInfo }

/**
 * A sub-agent's steps in order, with its calls looked up in the reply's list. Calls made back to back are one run;
 * a nested dispatch is its own step. Calls under the dispatch that its steps do not list (an older backend, a step
 * that was lost) are added at the end, so none is hidden.
 */
export function subAgentSteps(dispatch: ToolCallInfo, toolCalls: readonly ToolCallInfo[] | null | undefined): SubAgentStepView[] {
  const calls = toolCalls ?? []
  const byId = new Map(calls.filter((c) => c.id).map((c) => [c.id as string, c]))
  const steps: SubAgentStepView[] = []
  const listed = new Set<string>()

  const addCall = (call: ToolCallInfo): void => {
    if (call.id) listed.add(call.id)
    if (isSubAgentCall(call)) {
      steps.push({ kind: 'sub-agent', toolCall: call })
      return
    }
    const previous = steps[steps.length - 1]
    if (previous?.kind === 'tools') steps[steps.length - 1] = { kind: 'tools', toolCalls: [...previous.toolCalls, call] }
    else steps.push({ kind: 'tools', toolCalls: [call] })
  }

  for (const step of dispatch.task?.steps ?? []) {
    if (step.type === 'text') {
      if (step.text?.trim()) steps.push({ kind: 'text', text: step.text })
      continue
    }
    const call = step.toolCallId ? byId.get(step.toolCallId) : undefined
    if (call && !listed.has(call.id as string)) addCall(call)
  }

  for (const call of calls) {
    if (dispatch.id && call.parentId === dispatch.id && !(call.id && listed.has(call.id))) addCall(call)
  }
  return steps
}

/** How many tool calls a sub-agent made: what the CLI reported, else the calls under it. */
export function taskToolCount(dispatch: ToolCallInfo, toolCalls: readonly ToolCallInfo[] | null | undefined): number {
  const reported = dispatch.task?.toolUseCount
  if (typeof reported === 'number') return reported
  return (toolCalls ?? []).filter((c) => !!dispatch.id && c.parentId === dispatch.id).length
}

/**
 * How long a task has run, in ms: for a running one the time since it started (never less than what the CLI last
 * reported), for a finished one what the CLI reported. Null when neither is known.
 */
export function taskElapsedMs(detail: ToolTaskDetailDto | null | undefined, now: number = Date.now()): number | null {
  if (!detail) return null
  const reported = typeof detail.durationMs === 'number' ? detail.durationMs : null
  if (taskStatus(detail) !== 'running') return reported
  const started = detail.startedAt ? Date.parse(detail.startedAt) : Number.NaN
  if (Number.isNaN(started)) return reported
  return Math.max(reported ?? 0, now - started)
}

/** "8s", "6m", "1h 02m": short enough for the end of a row. */
export function formatTaskDuration(ms: number | null | undefined): string {
  if (ms == null || !Number.isFinite(ms) || ms < 0) return ''
  const seconds = Math.floor(ms / 1000)
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m`
  return `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m`
}

/** The name a task goes by: its description, else a sub-agent's type, else a command's text. */
export function taskTitle(call: ToolCallInfo): string {
  const detail = call.task
  const command = call.input && typeof call.input === 'object' ? (call.input as Record<string, unknown>).command : undefined
  return detail?.description?.trim()
    || detail?.agentType?.trim()
    || (typeof command === 'string' ? command.trim() : '')
    || ''
}

const isStandIn = (call: ToolCallInfo): boolean => !call.name

/**
 * Merge two reports of one task: a value wins over its absence, the newer value over the older. Steps are the newer
 * list when it extends the older one (the backend sends the whole list each time); otherwise the steps the older list
 * lacks are added after it (a stand-in from a later run carries only what happened there).
 */
export function mergeTaskDetail(
  older: ToolTaskDetailDto | null | undefined,
  newer: ToolTaskDetailDto | null | undefined,
): ToolTaskDetailDto | null {
  if (!older) return newer ?? null
  if (!newer) return older
  const merged: ToolTaskDetailDto = { ...older }
  for (const [key, value] of Object.entries(newer) as [keyof ToolTaskDetailDto, unknown][]) {
    if (value !== null && value !== undefined && key !== 'steps') (merged as Record<string, unknown>)[key] = value
  }
  // An outcome is not undone by a later report that it is running (a stand-in that saw only progress)
  if (taskStatus(older) !== 'running' && taskStatus(newer) === 'running') merged.status = older.status
  merged.steps = mergeSteps(older.steps ?? [], newer.steps ?? [])
  return merged
}

const sameStep = (a: ToolTaskStepDto, b: ToolTaskStepDto): boolean =>
  a.type === b.type && (a.type === 'text' ? a.text === b.text : a.toolCallId === b.toolCallId)

function mergeSteps(older: readonly ToolTaskStepDto[], newer: readonly ToolTaskStepDto[]): ToolTaskStepDto[] {
  if (newer.length >= older.length && older.every((step, i) => sameStep(step, newer[i]!))) return [...newer]
  return [...older, ...newer.filter((step) => !older.some((o) => sameStep(o, step)))]
}

/**
 * Apply an update of a call to the call as shown. A stand-in update (no `name`) brings only the task's state; the
 * call's own name, input, place and outcome stay.
 */
export function mergeToolCall(current: ToolCallInfo, update: ToolCallInfo): ToolCallInfo {
  const task = mergeTaskDetail(current.task, update.task)
  if (isStandIn(update) && !isStandIn(current)) return { ...current, ...(task ? { task } : {}) }
  return { ...current, ...update, ...(task ? { task } : {}) }
}

/** Upsert calls into a list by id (calls without an id are appended). Returns a new list. */
export function upsertToolCalls(list: readonly ToolCallInfo[], updates: readonly ToolCallInfo[]): ToolCallInfo[] {
  let next = [...list]
  for (const update of updates) {
    const index = update.id ? next.findIndex((c) => c.id === update.id) : -1
    next = index === -1 ? [...next, { ...update }] : next.map((c, i) => (i === index ? mergeToolCall(c, update) : c))
  }
  return next
}

/**
 * The message, other than `exceptId`, that a call update belongs to: the one that holds a call with its id, or the
 * dispatch it was made in (its `parentId`). Null when it belongs to none (it is new, for the message streaming now).
 */
export function findToolCallOwner(
  messages: readonly ChatMessage[],
  update: ToolCallInfo,
  exceptId?: string,
): ChatMessage | null {
  const keys = [update.id, update.parentId].filter((k): k is string => !!k)
  for (const key of keys) {
    const owner = messages.find((m) => m.id !== exceptId && (m.toolCalls ?? []).some((c) => c.id === key && !isStandIn(c)))
    if (owner) return owner
  }
  return null
}

/**
 * Fold stand-in entries into the message that holds the real call: the stand-in and the calls made under it move
 * there, merged by id. Messages are returned as new objects only where something changed.
 */
export function mergeTaskCalls(messages: readonly ChatMessage[]): ChatMessage[] {
  let result = [...messages]
  for (let i = 0; i < result.length; i++) {
    const message = result[i]!
    const standIns = (message.toolCalls ?? []).filter((c) => isStandIn(c) && c.task && c.id)
    for (const standIn of standIns) {
      const ownerIndex = result.findIndex(
        (m, j) => j !== i && (m.toolCalls ?? []).some((c) => c.id === standIn.id && !isStandIn(c)),
      )
      if (ownerIndex === -1) continue
      const moving = new Set([standIn.id as string, ...descendants(message.toolCalls ?? [], standIn.id as string)])
      const moved = (message.toolCalls ?? []).filter((c) => c.id && moving.has(c.id))
      const owner = result[ownerIndex]!
      result = result.map((m, j) => {
        if (j === ownerIndex) return { ...owner, toolCalls: upsertToolCalls(owner.toolCalls ?? [], moved) }
        if (j === i) return { ...m, toolCalls: (m.toolCalls ?? []).filter((c) => !(c.id && moving.has(c.id))) }
        return m
      })
    }
  }
  return result
}

function descendants(calls: readonly ToolCallInfo[], id: string): string[] {
  const found: string[] = []
  const queue = [id]
  while (queue.length) {
    const parent = queue.shift() as string
    for (const call of calls) {
      if (call.parentId === parent && call.id && !found.includes(call.id)) {
        found.push(call.id)
        queue.push(call.id)
      }
    }
  }
  return found
}

/** One task of a conversation, for a list of them (`TBackgroundTasks`) or a sub-agent's own view. */
export interface TaskEntry {
  /** The id of the call that started it. */
  readonly id: string
  /** The message that holds the call. */
  readonly messageId: string
  readonly toolCall: ToolCallInfo
  readonly detail: ToolTaskDetailDto
  readonly status: TaskStatus
  /** `agent` for a sub-agent, `command` for a background command. */
  readonly kind: 'agent' | 'command'
  /** 0 for one the main agent started, 1 for one a sub-agent dispatched, and so on. */
  readonly depth: number
}

/**
 * Every sub-agent and background command of a conversation, in the order they were started, a nested sub-agent
 * after the one that dispatched it.
 */
export function collectTasks(messages: readonly ChatMessage[]): TaskEntry[] {
  const entries: TaskEntry[] = []
  const seen = new Set<string>()
  for (const message of messages) {
    const calls = message.toolCalls ?? []
    const listed = calls.filter((c) => c.id && !isStandIn(c) && isListedTask(c))
    const ids = new Set(listed.map((c) => c.id as string))
    const visit = (call: ToolCallInfo, depth: number): void => {
      const id = call.id as string
      if (seen.has(id)) return
      seen.add(id)
      entries.push({
        id,
        messageId: message.id,
        toolCall: call,
        detail: call.task!,
        status: taskStatus(call.task),
        kind: isSubAgentCall(call) ? 'agent' : 'command',
        depth,
      })
      for (const child of listed.filter((c) => c.parentId === id)) visit(child, depth + 1)
    }
    for (const call of listed.filter((c) => !c.parentId || !ids.has(c.parentId))) visit(call, 0)
  }
  return entries
}

/** The call with this id and the message that holds it, among the conversation's messages. */
export function findToolCall(
  messages: readonly ChatMessage[],
  id: string,
): { readonly toolCall: ToolCallInfo; readonly message: ChatMessage } | null {
  for (const message of messages) {
    const toolCall = (message.toolCalls ?? []).find((c) => c.id === id && !isStandIn(c))
    if (toolCall) return { toolCall, message }
  }
  return null
}
