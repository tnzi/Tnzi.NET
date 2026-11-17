import type { ChatMessage } from '../../headless/useChat'

/**
 * Whether `message` is an answer the agent went on with by itself: an assistant
 * row right after another assistant row, with no user message between them. An
 * external CLI agent does that when a background task it waited on finishes
 * (`turn_completed`, then a new answer), and without a mark between the two the
 * second reads as the first one shown twice.
 *
 * Only once the second answer has something on screen (text or a tool call):
 * a row still waiting on the tasks, or one whose first words have not come,
 * gets no mark, so the mark does not stand over an empty line.
 */
export function continuesAfterBackgroundTask(previous: ChatMessage | undefined, message: ChatMessage): boolean {
  if (previous?.role !== 'assistant' || message.role !== 'assistant') return false
  if (message.status === 'waiting') return false
  return message.content.trim() !== '' || (message.toolCalls?.length ?? 0) > 0
}
