/**
 * useReasoningActivity - is the message's reasoning what is arriving right now?
 *
 * A message streams for its whole turn, and in an agent turn that is long: the
 * model thinks, writes, calls tools, and may think again. Tying a thinking
 * block's "running" state to `message.isStreaming` kept it spinning for the
 * whole turn, well after later text and tool calls had arrived below it, which
 * reads as stuck.
 *
 * The answer here follows what grew last:
 * - the reasoning grew while the message streams: thinking is running;
 * - the text or the list of tool calls grew: thinking has stopped, even though
 *   the message still streams;
 * - both grew in the same update: the later output wins (it is the more
 *   likely order, and a wrong "running" is the worse mistake; the next
 *   reasoning delta turns it back on);
 * - the message is not streaming (finished, stopped, failed, or loaded from
 *   history): thinking is done.
 *
 * Reasoning is one concatenated string per message, so a turn that goes back
 * to thinking after writing or calling tools appends to the same text: the
 * block turns running again while the new thinking arrives and done once later
 * output follows. Where in the turn each piece of thinking happened is not in
 * the data, so it cannot be shown.
 *
 * On first use (a mounted message, a remount) there is no history to compare
 * against: reasoning counts as running only when nothing else has arrived yet.
 */
import { computed, ref, watch, type ComputedRef } from 'vue';
import type { ChatMessage } from './useChat';

/** The parts of a message this reads. */
export type ReasoningActivitySource = Pick<ChatMessage, 'reasoning' | 'content' | 'toolCalls' | 'isStreaming'>;

export interface UseReasoningActivityReturn {
  /** True while the reasoning is what is arriving. */
  isReasoning: ComputedRef<boolean>;
}

const outputSize = (m: ReasoningActivitySource) => (m.content?.length ?? 0) + (m.toolCalls?.length ?? 0);
const reasoningSize = (m: ReasoningActivitySource) => m.reasoning?.length ?? 0;

export function useReasoningActivity(message: () => ReasoningActivitySource): UseReasoningActivityReturn {
  const initial = message();
  const active = ref(reasoningSize(initial) > 0 && outputSize(initial) === 0);

  watch(
    () => [reasoningSize(message()), outputSize(message())] as const,
    ([reasoning, output], [prevReasoning, prevOutput]) => {
      if (output > prevOutput) active.value = false;
      else if (reasoning > prevReasoning) active.value = true;
      // A row reused for another answer (cleared, then streamed again).
      else if (reasoning === 0) active.value = false;
    },
  );

  const isReasoning = computed(() => (message().isStreaming ?? false) && active.value);
  return { isReasoning };
}
