/**
 * Which sub-agent's own view is open, shared by the places that open it (a sub-agent's step in a run, a row of
 * `TBackgroundTasks`) and the panel that shows it (`TSubAgentPanel`).
 *
 * A sub-agent's work is its own conversation: the prompt it was given, what it is doing, the steps it took and the
 * report it handed back. The main conversation shows it as one step of a run; opening that step shows the rest
 * beside the conversation, in a panel the product places (`TChatApp` puts it in its side panel, see
 * `subAgentAside`). Where no panel is placed (`panel` answers false), the step opens in place instead, inside its run.
 *
 * The view also carries how to stop a task (a sub-agent or a background command) of the conversation, so the panel
 * and the list offer a Stop button only where stopping works.
 */
import { computed, inject, provide, ref, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type { ToolCallInfo } from '../../headless/useChat'

/** Stops one task of the conversation; resolves whether the server took the request. */
export type StopTaskHandler = (taskId: string) => Promise<boolean> | boolean | void

export interface SubAgentView {
  /** The sub-agent shown (the id of the call that dispatched it); null when none is open. */
  readonly current: Readonly<Ref<string | null>>
  /** The sub-agents opened on the way to the current one (a nested one opened from its parent's view), oldest first. */
  readonly trail: Readonly<Ref<readonly string[]>>
  /** Whether a panel shows the open sub-agent; when not, steps open in place. */
  readonly panel: ComputedRef<boolean>
  /** Stops a task; null when the conversation cannot. */
  readonly stopTask: ComputedRef<StopTaskHandler | null>
  /** Task ids asked to stop, until the task says it stopped. */
  readonly stopping: Readonly<Ref<ReadonlySet<string>>>
  /** Show a sub-agent. From another sub-agent's view (`from`), Back returns there. */
  open(id: string, options?: { readonly from?: string | null }): void
  /** Return to the sub-agent this one was opened from; closes when there is none. */
  back(): void
  close(): void
  /** Ask to stop a task; marks it stopping until `stopped` is called or the request is refused. */
  stop(taskId: string): Promise<boolean>
  /** The task said it stopped (or finished): it is no longer stopping. */
  settled(taskId: string): void
}

export interface SubAgentViewOptions {
  /** Whether a panel shows the open sub-agent. Default: true. */
  readonly panel?: () => boolean
  /** How to stop a task; none = no Stop buttons. */
  readonly stopTask?: () => StopTaskHandler | null | undefined
}

/** A view's state. `TChatApp` makes one and provides it; a custom shell does the same with `provideSubAgentView`. */
export function createSubAgentView(options: SubAgentViewOptions = {}): SubAgentView {
  const current = ref<string | null>(null)
  const trail = ref<string[]>([])
  const stopping = ref<ReadonlySet<string>>(new Set())
  const panel = computed(() => options.panel?.() ?? true)
  const stopTask = computed(() => options.stopTask?.() ?? null)

  const without = (id: string): ReadonlySet<string> => {
    const next = new Set(stopping.value)
    next.delete(id)
    return next
  }

  return {
    current,
    trail,
    panel,
    stopTask,
    stopping,
    open(id, openOptions) {
      if (openOptions?.from && openOptions.from !== id) trail.value = [...trail.value, openOptions.from]
      else trail.value = []
      current.value = id
    },
    back() {
      const previous = trail.value[trail.value.length - 1]
      trail.value = trail.value.slice(0, -1)
      current.value = previous ?? null
    },
    close() {
      trail.value = []
      current.value = null
    },
    async stop(taskId) {
      const handler = stopTask.value
      if (!handler || !taskId) return false
      stopping.value = new Set([...stopping.value, taskId])
      let accepted = false
      try {
        accepted = (await handler(taskId)) !== false
      } catch {
        accepted = false
      }
      if (!accepted) stopping.value = without(taskId)
      return accepted
    },
    settled(taskId) {
      if (stopping.value.has(taskId)) stopping.value = without(taskId)
    },
  }
}

const SUB_AGENT_VIEW_KEY: InjectionKey<SubAgentView> = Symbol('tnzi-ai-sub-agent-view')

/** Provide a view to the subtree (steps, panel and list inside it share it). */
export function provideSubAgentView(view: SubAgentView): SubAgentView {
  provide(SUB_AGENT_VIEW_KEY, view)
  return view
}

/** The view provided above; null outside one (steps then open in place and nothing offers Stop). */
export function useSubAgentView(): SubAgentView | null {
  return inject(SUB_AGENT_VIEW_KEY, null)
}

const MESSAGE_TOOL_CALLS_KEY: InjectionKey<Readonly<Ref<readonly ToolCallInfo[]>>> = Symbol('tnzi-ai-message-tool-calls')

/** The tool calls of the message being drawn (a sub-agent's own calls are found among them). */
export function provideMessageToolCalls(toolCalls: Readonly<Ref<readonly ToolCallInfo[]>>): void {
  provide(MESSAGE_TOOL_CALLS_KEY, toolCalls)
}

/** The tool calls of the message being drawn; empty outside a message. */
export function useMessageToolCalls(): Readonly<Ref<readonly ToolCallInfo[]>> {
  return inject(MESSAGE_TOOL_CALLS_KEY, ref([]))
}
