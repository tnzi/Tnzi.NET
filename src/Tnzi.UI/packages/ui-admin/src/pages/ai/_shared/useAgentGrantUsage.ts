import { ref, type Ref } from 'vue'
import type {
  AgentGrantBridge,
  AgentGrantResourceType,
  AgentGrantUsageDto,
} from '../../../services/bridges/agent-grant-bridge'

/**
 * Which agents hold an active grant for one resource (skill / knowledge base /
 * tool key), as a four-state value.
 *
 * `error` is a state of its own, never folded into an empty list: the answer is
 * read right before an operator changes or deletes the resource, and "the check
 * failed" must not look like "no agent depends on this".
 */
export type GrantUsageState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'loaded'; agents: AgentGrantUsageDto[] }
  | { status: 'error'; message: string }

export interface AgentGrantUsage {
  state: Ref<GrantUsageState>
  /** Load (or reload) usage for a resource. A newer call supersedes an in-flight one. */
  load(type: AgentGrantResourceType, key: string): Promise<void>
  reset(): void
}

export function useAgentGrantUsage(bridge: AgentGrantBridge): AgentGrantUsage {
  const state = ref<GrantUsageState>({ status: 'idle' })
  // 只认最后一次请求：先后打开两张卡的删除确认时，慢的旧响应不能盖掉新的。
  let sequence = 0

  async function load(type: AgentGrantResourceType, key: string): Promise<void> {
    const current = ++sequence
    state.value = { status: 'loading' }
    try {
      const agents = await bridge.usedBy(type, key)
      if (current === sequence) state.value = { status: 'loaded', agents }
    } catch (e) {
      if (current !== sequence) return
      const message = e instanceof Error && e.message ? e.message : 'Request failed'
      state.value = { status: 'error', message }
    }
  }

  function reset(): void {
    sequence++
    state.value = { status: 'idle' }
  }

  return { state, load, reset }
}
