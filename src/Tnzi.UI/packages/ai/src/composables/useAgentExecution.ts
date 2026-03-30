/**
 * useAgentExecution — Agent run state tracking
 *
 * Tracks the live execution state of an AI agent, including tool calls,
 * agent switches (Handoff), and reasoning deltas. Processes SSE events
 * from @tnzi/core streaming.
 */

import { ref, readonly, type Ref, type DeepReadonly } from 'vue';
import { scheduleFrame } from '@/lib/scheduleFrame';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface AgentToolCall {
  /** Tool name. */
  name: string;
  /** Start timestamp. */
  startedAt: string;
  /** End timestamp (null if still running). */
  endedAt: string | null;
  /** Duration in milliseconds (null if still running). */
  durationMs: number | null;
  /** Tool input (JSON string). */
  input?: string | null;
  /** Tool output (JSON string). */
  output?: string | null;
}

export interface HandoffEntry {
  /** Agent name that was switched to. */
  agentName: string;
  /** Timestamp of the switch. */
  timestamp: string;
}

export interface AgentExecutionEvent {
  /** Event type — maps to SSE event types from the backend. */
  type:
    | 'agent_switch'
    | 'tool_call_start'
    | 'tool_call_end'
    | 'reasoning_delta'
    | 'delta'
    | 'done'
    | 'error';
  /** Agent name (for agent_switch). */
  agentName?: string;
  /** Tool names (for tool_call_start). */
  toolNames?: string[];
  /** Tool call details (for tool_call_end). */
  toolCalls?: Array<{
    name: string;
    durationMs?: number | null;
    input?: string | null;
    output?: string | null;
  }>;
  /** Reasoning text (for reasoning_delta). */
  reasoningDelta?: string;
  /** Error message. */
  errorMessage?: string;
}

export interface UseAgentExecutionReturn {
  /** Current agent name. */
  currentAgent: DeepReadonly<Ref<string | null>>;
  /** Active and completed tool calls. */
  toolCalls: DeepReadonly<Ref<readonly AgentToolCall[]>>;
  /** History of agent switches. */
  handoffHistory: DeepReadonly<Ref<readonly HandoffEntry[]>>;
  /** Whether execution is in progress. */
  isExecuting: DeepReadonly<Ref<boolean>>;
  /** Accumulated reasoning text. */
  reasoning: DeepReadonly<Ref<string>>;
  /** Process an SSE event. */
  handleEvent: (event: AgentExecutionEvent) => void;
  /** Reset all state. */
  reset: () => void;
}

// ---------------------------------------------------------------------------
// Composable
// ---------------------------------------------------------------------------

export function useAgentExecution(): UseAgentExecutionReturn {
  const currentAgent = ref<string | null>(null);
  const toolCalls = ref<readonly AgentToolCall[]>([]);
  const handoffHistory = ref<readonly HandoffEntry[]>([]);
  const isExecuting = ref(false);
  const reasoning = ref('');

  // rAF batching for high-frequency reasoning deltas
  let reasoningBuffer = '';
  let reasoningPending = false;

  function flushReasoning(): void {
    reasoningPending = false;
    if (reasoningBuffer) {
      reasoning.value = reasoning.value + reasoningBuffer;
      reasoningBuffer = '';
    }
  }

  function handleEvent(event: AgentExecutionEvent): void {
    if (!isExecuting.value && event.type !== 'done' && event.type !== 'error') {
      isExecuting.value = true;
    }

    switch (event.type) {
      case 'agent_switch': {
        const name = event.agentName ?? 'unknown';
        currentAgent.value = name;
        handoffHistory.value = [
          ...handoffHistory.value,
          { agentName: name, timestamp: new Date().toISOString() },
        ];
        break;
      }

      case 'tool_call_start': {
        const names = event.toolNames ?? [];
        const newCalls: AgentToolCall[] = names.map((name) => ({
          name,
          startedAt: new Date().toISOString(),
          endedAt: null,
          durationMs: null,
        }));
        toolCalls.value = [...toolCalls.value, ...newCalls];
        break;
      }

      case 'tool_call_end': {
        const details = event.toolCalls ?? [];
        const now = new Date().toISOString();
        // Build a Map for O(1) lookup instead of nested find
        const detailMap = new Map(details.map((d) => [d.name, d]));
        toolCalls.value = toolCalls.value.map((tc) => {
          if (tc.endedAt !== null) return tc;
          const match = detailMap.get(tc.name);
          if (match) {
            return {
              ...tc,
              endedAt: now,
              durationMs: match.durationMs ?? null,
              input: match.input ?? tc.input,
              output: match.output ?? tc.output,
            };
          }
          return tc;
        });
        break;
      }

      case 'reasoning_delta': {
        reasoningBuffer = reasoningBuffer + (event.reasoningDelta ?? '');
        if (!reasoningPending) {
          reasoningPending = true;
          scheduleFrame(flushReasoning);
        }
        break;
      }

      case 'done': {
        // Flush any pending reasoning before marking done
        if (reasoningPending) {
          flushReasoning();
        }
        isExecuting.value = false;
        break;
      }

      case 'error': {
        if (reasoningPending) {
          flushReasoning();
        }
        isExecuting.value = false;
        break;
      }
    }
  }

  function reset(): void {
    currentAgent.value = null;
    toolCalls.value = [];
    handoffHistory.value = [];
    isExecuting.value = false;
    reasoning.value = '';
    reasoningBuffer = '';
    reasoningPending = false;
  }

  return {
    currentAgent: readonly(currentAgent),
    toolCalls: readonly(toolCalls) as DeepReadonly<Ref<readonly AgentToolCall[]>>,
    handoffHistory: readonly(handoffHistory) as DeepReadonly<Ref<readonly HandoffEntry[]>>,
    isExecuting: readonly(isExecuting),
    reasoning: readonly(reasoning),
    handleEvent,
    reset,
  };
}
