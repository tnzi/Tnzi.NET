/**
 * How a CLI task reads as a step of a run: a sub-agent the agent dispatched, or a command it runs in the background.
 *
 * Such a step is counted in its run's one line like any other ("Finished 6 background commands, read 2 files"),
 * by what happened to the task rather than to the call that started it: a background command's call returns at
 * once, but the command is "running" until its task says otherwise, and the line says so ("A background command
 * running"). A stopped task is counted apart ("Stopped an agent"), not as a failure.
 */
import { formatAiMessage } from '../../i18n/index'
import type { AiLocaleMessages } from '../../locales/en'
import type { ToolCallInfo } from '../../headless/useChat'
import { isBackgroundCommand, isSubAgentCall, taskStatus, taskTitle } from '../../headless/cli-tasks'
import type { ToolCallState } from './tool-call-status'
import type { ToolStepDescription } from './tool-steps'

/** Whether a call reads as a task step: a sub-agent, or a command in the background. */
export function isTaskStep(call: ToolCallInfo): boolean {
  return isSubAgentCall(call) || isBackgroundCommand(call)
}

/** A task step's words, by its kind and state. */
export function taskStepDescription(call: ToolCallInfo, messages: AiLocaleMessages): ToolStepDescription {
  const t = messages.tasks
  const agent = isSubAgentCall(call)
  const kind = agent ? 'task:agent' : 'task:command'
  const subject = taskTitle(call) || null
  const count = (template: string) => (n: number) => formatAiMessage(template, { count: n })
  if (taskStatus(call.task) === 'stopped') {
    return {
      running: agent ? t.agentRunning : t.commandRunning,
      done: agent ? t.agentStopped : t.commandStopped,
      failed: agent ? t.agentFailed : t.commandFailed,
      many: count(agent ? t.agentsStopped : t.commandsStopped),
      kind: `${kind}:stopped`,
      subject,
    }
  }
  return {
    running: agent ? t.agentRunning : t.commandRunning,
    done: agent ? t.agentDone : t.commandDone,
    failed: agent ? t.agentFailed : t.commandFailed,
    many: count(agent ? t.agentsDone : t.commandsDone),
    ongoing: agent ? t.agentOngoing : t.commandOngoing,
    manyOngoing: count(agent ? t.agentsOngoing : t.commandsOngoing),
    kind,
    subject,
  }
}

/** A task step's state: the task's own, not its call's (a stopped task is a finished step with its own words). */
export function taskStepState(call: ToolCallInfo): ToolCallState {
  switch (taskStatus(call.task)) {
    case 'failed':
      return 'failed'
    case 'running':
      return 'running'
    default:
      return 'completed'
  }
}
