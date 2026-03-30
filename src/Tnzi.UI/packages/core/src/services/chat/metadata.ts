/**
 * Chat Module Metadata
 * Aligned with Tnzi.Chat/Entities/Message.cs
 */

/**
 * Message type (visibility)
 * Backend: MessageType enum
 */
export enum MessageType {
  /** 公共消息（发送给角色） */
  Public = 1,
  /** 私人消息（发送给指定用户） */
  Private = 2,
}

/**
 * Get message type label
 */
export function getMessageTypeLabel(type: MessageType): string {
  switch (type) {
    case MessageType.Public:
      return 'Public';
    case MessageType.Private:
      return 'Private';
    default:
      return 'Unknown';
  }
}
