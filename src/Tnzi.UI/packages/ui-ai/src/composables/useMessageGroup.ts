/**
 * useMessageGroup - Message grouping logic
 *
 * Groups consecutive chat messages for rendering:
 * - Each user message gets its own group ('human')
 * - Consecutive assistant + tool messages are merged into a single 'assistant' group
 * - System messages get their own group ('system')
 */

import type { ChatMessage } from './useChat';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type MessageGroupType = 'human' | 'assistant' | 'system';

export interface MessageGroup {
  /** Unique group ID (derived from first message ID). */
  id: string;
  /** Group type. */
  type: MessageGroupType;
  /** Messages in this group. */
  messages: readonly ChatMessage[];
}

// ---------------------------------------------------------------------------
// Logic
// ---------------------------------------------------------------------------

function getGroupType(role: string): MessageGroupType {
  switch (role) {
    case 'user':
      return 'human';
    case 'assistant':
    case 'tool':
      return 'assistant';
    case 'system':
      return 'system';
    default:
      return 'assistant';
  }
}

/**
 * Group messages for display.
 *
 * - Each user message starts a new 'human' group
 * - Consecutive assistant / tool messages are merged into one 'assistant' group
 * - Each system message gets its own 'system' group
 */
export function groupMessages(messages: readonly ChatMessage[]): MessageGroup[] {
  if (messages.length === 0) return [];

  const groups: MessageGroup[] = [];
  let currentGroup: { type: MessageGroupType; messages: ChatMessage[] } | null = null;

  for (const message of messages) {
    const type = getGroupType(message.role);

    if (type === 'human' || type === 'system') {
      // User and system messages always start a new group
      if (currentGroup) {
        groups.push({
          id: currentGroup.messages[0]?.id ?? '',
          type: currentGroup.type,
          messages: currentGroup.messages,
        });
      }
      groups.push({
        id: message.id,
        type,
        messages: [message],
      });
      currentGroup = null;
    } else {
      // assistant / tool - merge into current assistant group
      if (currentGroup && currentGroup.type === 'assistant') {
        currentGroup = {
          type: currentGroup.type,
          messages: [...currentGroup.messages, message],
        };
      } else {
        if (currentGroup) {
          groups.push({
            id: currentGroup.messages[0]?.id ?? '',
            type: currentGroup.type,
            messages: currentGroup.messages,
          });
        }
        currentGroup = { type: 'assistant', messages: [message] };
      }
    }
  }

  // Flush remaining group
  if (currentGroup) {
    groups.push({
      id: currentGroup.messages[0]?.id ?? '',
      type: currentGroup.type,
      messages: currentGroup.messages,
    });
  }

  return groups;
}
